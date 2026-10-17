using MoonSharp.Interpreter;
using NekoLua.Loader;

namespace NekoLua.Api;

/// <summary>
/// 解析并规范化来自 Lua 的文件路径，基于模块目录解析相对路径，并检验路径是否属于禁止访问的根路径集合。
/// </summary>
public static class LuaPathGuard
{
    internal static void Refresh()
    {
        Roots = null;
    }

    private static string[] Roots { get => field ??= BuildRoots(); set; }
    /// <summary>
    /// 解析输入路径并返回其规范化的绝对路径。相对路径基于 mod.ModDir 解析。
    /// </summary>
    /// <param name="mod">用于解析相对路径的 LuaModInstance，其 ModDir 作为基准目录。</param>
    /// <param name="input">要解析的路径；可以是相对或绝对路径，不能为空或仅包含空白字符。</param>
    /// <param name="prefix">用于构造异常消息的前缀文本。</param>
    /// <returns>解析并返回输入的规范化绝对路径。</returns>
    /// <exception cref="ScriptRuntimeException">当 input 为空或仅空白时或当解析后的路径被禁止访问时抛出。</exception>
    public static string Resolve(LuaModInstance mod, string input, string prefix)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ScriptRuntimeException($"{prefix}: 路径不能为空");

        if (!Path.IsPathRooted(input))
            input = Path.Combine(mod.ModDir, input);

        var full = Path.GetFullPath(input);

        return IsBlocked(full) ? throw new ScriptRuntimeException($"{prefix}: 禁止访问系统路径") : full;
    }
    /// <summary>
    /// 确定指定完整路径是否等于或位于预定义禁止访问的根路径之一之下。
    /// </summary>
    /// <param name="fullPath">要检查的完整文件或目录路径。</param>
    /// <returns>当路径等于某个根路径或位于该根路径下时返回 true；否则返回 false。</returns>
    public static bool IsBlocked(string fullPath)
    {
        foreach (var root in Roots)
        {
            if (fullPath.Equals(root, StringComparison.OrdinalIgnoreCase))
                return true;

            if (fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
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
    private static void AddIfValid(List<string> list, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;

        string normalized;
        try
        {
            normalized = Path.GetFullPath(raw)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return;
        }

        if (!list.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            list.Add(normalized);
    }
}
