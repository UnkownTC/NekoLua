using LabApi.Features.Console;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies;

/// <summary>
/// mod 表：
///   mod.name                        -- 当前 mod 名
///   mod.config                      -- 当前配置（table）
///   mod.registercfg(value)          -- 注册默认配置（table 或 json 字符串），
///                                      与磁盘深合并后落盘
/// </summary>
public class LuaModProxy : IRegisterLua
{
    public string Name { get; } = "mod";
    public const string ReservedConfigKey = "server_port";
    private const int MaxMergeDepth = 64;
    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var t = new Table(script);

        t["name"] = mod.Name;
        t["dir"] = mod.ModDir;

        var loaded = LoadFromDisk(mod);
        if (loaded != null)
        {
            t["config"] = DynValue.NewTable(loaded);
        }
        else
        {
            var empty = new Table(script);
            SaveToDisk(mod, empty);
            t["config"] = DynValue.NewTable(empty);
        }

        t["registercfg"] = DynValue.NewCallback((_, args) => RegisterCfg(mod, t, args));

        return t;
    }
    static DynValue RegisterCfg(LuaModInstance mod, Table modTable, CallbackArguments args)
    {
        if (args.Count < 1)
            throw new ScriptRuntimeException("mod.registercfg(value): 需要 table 或 json 字符串");

        Table defaults;

        switch (args[0].Type)
        {
            case DataType.Table:
                defaults = args[0].Table;
                break;

            case DataType.String:
                try
                {
                    var dyn = LuaJson.FromJson(mod.Script, args[0].String);
                    if (dyn.Type != DataType.Table)
                        throw new ScriptRuntimeException("mod.registercfg: json 根必须是对象");
                    defaults = dyn.Table;
                }
                catch (ScriptRuntimeException) { throw; }
                catch (Exception ex)
                {
                    throw new ScriptRuntimeException($"mod.registercfg: json 解析失败: {ex.Message}");
                }
                break;

            default:
                throw new ScriptRuntimeException("mod.registercfg: 需要 table 或 json 字符串");
        }

        RejectReserved(defaults);

        var existing = modTable.Get("config").Table;
        var merged = DeepMerge(mod.Script, defaults, existing, 0);

        SaveToDisk(mod, merged);
        modTable["config"] = DynValue.NewTable(merged);
        return DynValue.Nil;
    }

    static void RejectReserved(Table cfg)
    {
        var v = cfg.Get(ReservedConfigKey);
        if (v.Type != DataType.Nil)
            throw new ScriptRuntimeException(
                $"mod.registercfg: '{ReservedConfigKey}' 是系统保留键，不能由 mod 声明");
    }
    static Table DeepMerge(Script script, Table baseT, Table overrideT, int depth)
    {
        if (depth > MaxMergeDepth)
            throw new ScriptRuntimeException($"mod.registercfg: 配置嵌套深度超过 {MaxMergeDepth}");

        var result = new Table(script);

        foreach (var p in baseT.Pairs)
            result[p.Key] = p.Value;

        foreach (var p in overrideT.Pairs)
        {
            var oldV = result.Get(p.Key);
            var newV = p.Value;

            result[p.Key] = oldV.Type == DataType.Table && newV.Type == DataType.Table
                ? DynValue.NewTable(DeepMerge(script, oldV.Table, newV.Table, depth + 1))
                : newV;
        }

        return result;
    }
    static Table? LoadFromDisk(LuaModInstance mod)
    {
        var path = GetConfigPath(mod);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var dyn = LuaJson.FromJson(mod.Script, json);
            if (dyn.Type != DataType.Table)
                return null;

            var t = dyn.Table;
            t[ReservedConfigKey] = DynValue.Nil;
            return t;
        }
        catch (Exception ex)
        {
            Logger.Error($"[{mod.Name}] 配置读取失败: {ex.Message}");
            return null;
        }
    }

    static void SaveToDisk(LuaModInstance mod, Table cfg)
    {
        var path = GetConfigPath(mod);
        try
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, LuaJson.ToJson(DynValue.NewTable(cfg)));
        }
        catch (Exception ex)
        {
            Logger.Error($"[{mod.Name}] 配置保存失败: {ex.Message}");
        }
    }

    static string GetConfigPath(LuaModInstance mod)
    {
        return Path.Combine(LuaPaths.Configs.FullName, mod.Name + ".json");
    }
}
