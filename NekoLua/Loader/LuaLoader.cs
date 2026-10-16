using LabApi.Features.Console;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Proxies;
using NekoLua.Registers;

namespace NekoLua.Loader;

public static class LuaLoader
{
    private static readonly List<LuaModInstance> _mods = [];
    public static IReadOnlyCollection<LuaModInstance> Mods => _mods;
    public static List<LuaModInstance> LoadAll()
    {
        LuaCommandHook.ClearAll();
        LuaEventHook.ClearAll();
        LuaEventBus.ClearAll();
        LuaTimer.ClearAll();
        LuaRegexProxy.ClearAll();
        LuaNetworkManager.ClearAll();
        LuaScheduler.ClearAll();
        LuaAsyncScheduler.ClearAll();

        _mods.Clear();
        LoadScope(LuaPaths.GlobalMods);
        LoadScope(LuaPaths.Mods);
        Logger.Info($"已加载 {_mods.Count} 个 Lua 模组");
        return _mods;
    }

    // 插件 Disable 时释放 mod 实例列表：否则旧 Script 及其全部数据
    // 被静态列表跨禁用持有（审查 P3-33）
    internal static void ClearAll()
    {
        _mods.Clear();
    }
    public static List<LuaModInstance> Refresh()
    {
        var existing = new Dictionary<string, LuaModInstance>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in _mods)
            existing[Path.GetFullPath(m.ModDir)] = m;

        var next = new List<LuaModInstance>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        RefreshScope(LuaPaths.GlobalMods, existing, seen, next);
        RefreshScope(LuaPaths.Mods, existing, seen, next);

        var removed = new List<LuaModInstance>();
        foreach (var m in _mods)
        {
            var key = Path.GetFullPath(m.ModDir);
            if (!seen.Contains(key))
                removed.Add(m);
        }

        foreach (var m in removed)
        {
            Logger.Info($"[NekoLua] 检测到 mod 目录已删除: {m.Name}");
            CleanupMod(m);
        }

        _mods.Clear();
        _mods.AddRange(next);

        return _mods;
    }

    static void LoadScope(DirectoryInfo root)
    {
        if (!root.Exists)
        { root.Create(); return; }

        foreach (var dir in root.GetDirectories())
        {
            var entry = Path.Combine(dir.FullName, "main.lua");
            if (!File.Exists(entry))
            {
                Logger.Warn($"跳过 {dir.Name}：缺少 main.lua");
                continue;
            }

            var mod = Build(dir.FullName, entry);
            if (mod != null)
                _mods.Add(mod);
        }
    }

    static void RefreshScope(
        DirectoryInfo root,
        Dictionary<string, LuaModInstance> existing,
        HashSet<string> seen,
        List<LuaModInstance> output)
    {
        if (!root.Exists)
        { root.Create(); return; }

        foreach (var dir in root.GetDirectories())
        {
            var entry = Path.Combine(dir.FullName, "main.lua");
            if (!File.Exists(entry))
            {
                Logger.Warn($"跳过 {dir.Name}：缺少 main.lua");
                continue;
            }

            var full = Path.GetFullPath(dir.FullName);
            seen.Add(full);

            if (existing.TryGetValue(full, out var known))
            {
                output.Add(known);
                continue;
            }
            Logger.Info($"[NekoLua] 发现新 mod: {dir.Name}");
            var mod = Build(full, entry);
            if (mod != null)
                output.Add(mod);
        }
    }

    static void CleanupMod(LuaModInstance mod)
    {
        LuaEventHook.CleanupFor(mod);
        LuaCommandHook.CleanupFor(mod);
        LuaEventBus.CleanupFor(mod);
        LuaRegexProxy.CleanupFor(mod);
        LuaNetworkManager.CleanupFor(mod);
        LuaTimer.CancelAll(mod);
        LuaScheduler.CancelAll(mod);
        LuaAsyncScheduler.CleanupFor(mod);
    }
    public static void Reload(LuaModInstance mod)
    {
        if (mod.Disabled)
            return;

        try
        {
            var code = File.ReadAllText(mod.EntryFile);
            _ = mod.Script.LoadString(code, null, mod.EntryFile);
        }
        catch (SyntaxErrorException ex)
        {
            Logger.Error($"[{mod.Name}] 语法错误，保留旧版本: {ex.DecoratedMessage}");
            return;
        }

        CleanupMod(mod);

        var ok = RunEntry(mod);
        Logger.Info(ok
            ? $"重载 Lua -> {mod.Name}"
            : $"重载 Lua -> {mod.Name} (失败)");
    }
    public static void ReloadAll(IEnumerable<LuaModInstance> mods)
    {
        foreach (var m in mods)
            Reload(m);
    }
    static LuaModInstance Build(string dir, string entry)
    {
        var name = Path.GetFileName(dir);

        var script = new Script(CoreModules.Preset_HardSandbox);
        script.Options.DebugPrint = s => Logger.Info($"[{name}] {s}");

        var mod = new LuaModInstance
        {
            Name = name,
            ModDir = dir,
            EntryFile = entry,
            Script = script,
        };

        RegisterApis(script, mod);
        RunEntry(mod);

        if (mod.Disabled)
        {
            CleanupMod(mod);
            return null;
        }

        return mod.Disabled ? null : mod;
    }

    static void RegisterApis(Script script, LuaModInstance mod)
    {
        script.Globals["require"] = BuildRequire(mod);

        foreach (var register in LuaRegister.Registers)
            script.Globals[register.Name] = register.Create(mod);
    }
    static bool RunEntry(LuaModInstance mod)
    {
        try
        {
            var code = File.ReadAllText(mod.EntryFile);
            _ = mod.Script.DoString(code, null, mod.EntryFile);
            return true;
        }
        catch (InterpreterException ex)
        {
            Logger.Error($"[{mod.Name}] 加载失败: {ex.DecoratedMessage}");
            mod.Disabled = true;
            return false;
        }
    }

    static DynValue BuildRequire(LuaModInstance mod)
    {
        return DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("require(name): 需要一个字符串");

            var name = args[0].String;
            if (name.Contains(".."))
                throw new ScriptRuntimeException("require: 非法路径");

            var rel = name.Replace('.', Path.DirectorySeparatorChar) + ".lua";

            var candidates = new (string file, string root)[]
            {
                (Path.Combine(mod.ModDir, rel),                   mod.ModDir),
                (Path.Combine(LuaPaths.Libs.FullName, rel),       LuaPaths.Libs.FullName),
                (Path.Combine(LuaPaths.GlobalLibs.FullName, rel), LuaPaths.GlobalLibs.FullName),
            };

            foreach (var (file, root) in candidates)
            {
                var full = Path.GetFullPath(file);
                var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(rootFull, StringComparison.Ordinal))
                    continue;
                if (!File.Exists(full))
                    continue;

                var code = File.ReadAllText(full);
                return mod.Script.DoString(code, null, full);
            }

            throw new ScriptRuntimeException($"require: 找不到 {name}");
        });
    }
}
