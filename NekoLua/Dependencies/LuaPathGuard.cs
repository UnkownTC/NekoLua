using MoonSharp.Interpreter;

using NekoLua.Loader;

namespace NekoLua.Dependencies;

public static class LuaPathGuard
{
    public static void Refresh()
    {
        Roots = null;
    }

    private static string[] Roots { get => field ??= BuildRoots(); set; }
    public static string Resolve(LuaModInstance mod, string input, string prefix)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ScriptRuntimeException($"{prefix}: 路径不能为空");

        if (!Path.IsPathRooted(input))
            input = Path.Combine(mod.ModDir, input);

        var full = Path.GetFullPath(input);

        return IsBlocked(full) ? throw new ScriptRuntimeException($"{prefix}: 禁止访问系统路径") : full;
    }
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
