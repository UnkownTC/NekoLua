using System.Runtime.InteropServices;

using MoonSharp.Interpreter;

using NekoLua.Loader;

namespace NekoLua.Dependencies;

/// <summary>
/// 文件/目录访问门禁（审查 P2-13）。
///
/// 采用「mod 作用域白名单 + 系统目录黑名单」双层模型：
/// <list type="number">
///   <item>解析后的绝对路径落在<b>调用方脚本自己的 mod 目录</b>内 → 放行（脚本私有工作区）；</item>
///   <item>命中 <c>Config.BlockedPaths</c>（Windows / 系统 / ProgramFiles / <c>/etc</c> 等）→ <b>拒绝</b>，
///         即使被 <c>Config.AllowedPaths</c> 放行也一样；</item>
///   <item>命中 <c>Config.AllowedPaths</c> → 放行（用于显式共享目录）；</item>
///   <item>其余一律拒绝（默认只允许访问自己的 mod 目录）。</item>
/// </list>
///
/// 相对路径仍以 <c>mod.ModDir</c> 为基准拼接。黑名单与放行表在 <c>Config</c> 热重载后由
/// <see cref="Refresh"/> 置空重建。
///
/// <para>
/// <b>符号链接 / junction / 挂载点穿透防护（审查 P3）</b>：<see cref="Path.GetFullPath(string)"/>
/// 只做<b>词法</b>规整（合并 <c>.</c>/<c>..</c>、补全根），<b>不会</b>解析 NTFS 重解析点。
/// 因此脚本只要在自己的 mod 目录内放一个指向 <c>C:\Windows</c> 的 junction/symlink，
/// 上面所有基于字符串的判定就会全部失效，直接绕过整个沙箱。
/// </para>
/// <para>
/// 修法：任何比较之前，先把路径解析到<b>真实路径</b>——用 Win32
/// <c>CreateFileW</c> + <c>GetFinalPathNameByHandle</c>（内核负责展开全部链接，
/// 链接环由内核报错，无需自行防环）。解析时对「尚未存在的文件」采取
/// 「<b>解析最深的已存在祖先，再把剩余字面量尾部拼回</b>」的策略，保证
/// <c>file.write("新建.txt")</c> 这类正常用法不受影响。
/// </para>
/// <para>
/// 另外两点值得说明：
/// <list type="bullet">
///   <item>本方法<b>返回解析后的真实路径</b>而不是脚本给的词法路径——这样调用方手里拿到的
///         就是已经过判定的目标，避免「判定的路径」与「真正使用的路径」不一致；</item>
///   <item>词法折叠 <c>..</c> 与内核「先展开链接再回退」的语义不同
///         （<c>mod\link\..\x</c> 在 Windows 上其实等于 <c>C:\x</c>）。因为返回的是解析结果且
///         默认拒绝，这种差异不会造成逃逸。</item>
/// </list>
/// </para>
/// <para>
/// <b>降级行为</b>：非 Windows 平台、或无权限打开句柄（解析失败）时退回词法路径，
/// 与旧实现一致，<b>不引入回归</b>；此时判定仍由「默认拒绝 + 黑名单」兜底。
/// </para>
/// </summary>
public static class LuaPathGuard
{
    public static void Refresh()
    {
        _roots = null;
        _allowList = null;
    }

    private static string[]? _roots;
    private static string[]? _allowList;

    private static string[] Roots => _roots ??= BuildRoots();
    private static string[] AllowList => _allowList ??= BuildAllowList();

    public static string Resolve(LuaModInstance mod, string input, string prefix)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ScriptRuntimeException($"{prefix}: 路径不能为空");

        if (mod is null)
            throw new ScriptRuntimeException($"{prefix}: 内部错误（缺少 mod 上下文）");

        if (!Path.IsPathRooted(input))
            input = Path.Combine(mod.ModDir, input);

        var full = Path.GetFullPath(input);

        // 审查 P3「符号链接 / junction 穿透」：先落到真实路径，后续判定一律基于真实路径。
        var real = TrimTrailingSeparators(ResolveRealPath(TrimTrailingSeparators(full)));

        // 1) 自身 mod 目录 —— 永远允许（两侧都取真实路径：mod 目录本身也可能是链接）
        var modRoot = TrimTrailingSeparators(ResolveRealPath(Normalize(mod.ModDir)));

        if (IsUnder(modRoot, real))
            return real;

        // 2) 系统 / 敏感目录 —— 永远拒绝
        if (IsBlockedCore(real))
            throw new ScriptRuntimeException($"{prefix}: 禁止访问系统路径");

        // 3) 显式放行表
        foreach (var root in AllowList)
        {
            if (IsUnder(root, real))
                return real;
        }

        // 4) 默认只允许自己的 mod 目录
        throw new ScriptRuntimeException(
            $"{prefix}: 只允许访问脚本自己的 mod 目录（如需共享目录请在 Config.AllowedPaths 中显式放行）");
    }

    /// <summary>
    /// 判断路径是否落在系统 / 敏感目录黑名单内。入参会被解析到真实路径后再判定，
    /// 因此链接指向黑名单目录的情况同样会被拦下。
    /// </summary>
    public static bool IsBlocked(string fullPath)
        => IsBlockedCore(TrimTrailingSeparators(
            ResolveRealPath(TrimTrailingSeparators(fullPath))));

    private static bool IsBlockedCore(string realTarget)
    {
        foreach (var root in Roots)
        {
            if (IsUnder(root, realTarget))
                return true;
        }

        return false;
    }

    private static bool IsUnder(string root, string target)
    {
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(target))
            return false;

        return target.Equals(root, StringComparison.OrdinalIgnoreCase)
            || target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
        => TrimTrailingSeparators(Path.GetFullPath(path));

    private static string TrimTrailingSeparators(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string[] BuildRoots()
    {
        var list = new List<string>();

        var cfg = MainPlugin.CacheConfig;

        if (cfg?.BlockedPaths is { Length: > 0 } extra)
        {
            foreach (var p in extra)
                AddIfValid(list, p);
        }

        return [.. list];
    }

    private static string[] BuildAllowList()
    {
        var list = new List<string>();

        var cfg = MainPlugin.CacheConfig;

        if (cfg?.AllowedPaths is { Length: > 0 } extra)
        {
            foreach (var p in extra)
                AddIfValid(list, p);
        }

        return [.. list];
    }

    private static void AddIfValid(List<string> list, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;

        string normalized;
        try
        {
            normalized = Path.GetFullPath(raw);
        }
        catch
        {
            return;
        }

        // 配置项本身也可能是链接：一并取真实路径，避免「真实路径 vs 词法路径」比较不上
        normalized = TrimTrailingSeparators(ResolveRealPath(TrimTrailingSeparators(normalized)));

        if (!list.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            list.Add(normalized);
    }

    // ------------------------------------------------------------------
    //  真实路径解析（NTFS 重解析点展开）
    // ------------------------------------------------------------------

    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint ShareAll = 0x00000001 | 0x00000002 | 0x00000004; // READ | WRITE | DELETE
    private static readonly IntPtr InvalidHandleValue = new(-1);

    private static readonly bool IsWindowsRuntime =
        Environment.OSVersion.Platform == PlatformID.Win32NT;

    /// <summary>
    /// 把路径解析到真实路径：解析最深的**已存在**祖先（内核展开全部链接），
    /// 再把不存在的字面量尾部拼回。无法解析时原样返回。
    /// </summary>
    internal static string ResolveRealPath(string fullPath)
    {
        if (!IsWindowsRuntime || string.IsNullOrEmpty(fullPath))
            return fullPath;

        // 尾部里不存在的那几段（从深到浅收集，回拼时反向）
        List<string>? tail = null;
        var current = fullPath;

        while (!string.IsNullOrEmpty(current))
        {
            if (TryGetFinalPath(current, out var real))
            {
                if (tail is null)
                    return real;

                for (var i = tail.Count - 1; i >= 0; i--)
                    real = Path.Combine(real, tail[i]);

                return real;
            }

            var parent = Path.GetDirectoryName(current);

            // 到根仍解析不出来：退回词法路径（与旧行为一致）
            if (string.IsNullOrEmpty(parent) ||
                parent.Equals(current, StringComparison.OrdinalIgnoreCase))
                return fullPath;

            (tail ??= []).Add(Path.GetFileName(current));
            current = parent;
        }

        return fullPath;
    }

    private static bool TryGetFinalPath(string path, out string real)
    {
        real = path;

        var handle = CreateFileW(
            path,
            0,                      // 只要句柄，不需要读写权限
            ShareAll,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics, // 允许以目录为对象打开
            IntPtr.Zero);

        if (handle == InvalidHandleValue)
            return false;

        try
        {
            var buf = new char[512];
            var len = GetFinalPathNameByHandleW(handle, buf, (uint)buf.Length, 0);

            if (len == 0)
                return false;

            // 返回值为「不含结尾 NUL 的长度」；>= 缓冲区长度说明被截断，扩容重试
            if (len >= buf.Length)
            {
                buf = new char[len + 1];
                len = GetFinalPathNameByHandleW(handle, buf, (uint)buf.Length, 0);

                if (len == 0 || len >= buf.Length)
                    return false;
            }

            real = StripExtendedPrefix(new string(buf, 0, (int)len));
            return true;
        }
        catch (Exception ex)
        {
            // 解析失败按「未解析」处理：调用方退回词法路径
            LuaDiagnostics.WarnOnce($"PathGuard: 解析真实路径失败（{path}）", ex);
            return false;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    /// <summary>去掉 Win32 扩展长度前缀：<c>\\?\C:\x</c> → <c>C:\x</c>，<c>\\?\UNC\s\v</c> → <c>\\s\v</c>。</summary>
    private static string StripExtendedPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
            return @"\\" + path.Substring(8);

        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return path.Substring(4);

        return path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        IntPtr hFile,
        [Out] char[] lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
