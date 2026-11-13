using MoonSharp.Interpreter;

using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies;

/// <summary>
/// env 表（环境变量）：
///
///   env.get(name)               -> string | nil
///   env.has(name)               -> bool
///   env.expand(str)             -> string    展开 %VAR%（Win）/ $VAR（Linux）
///   env.list()                  -> table     {name = value}
///
///   env.set(name, value)        -> bool      受 Config.EnvWriteEnabled 控制
///   env.delete(name)            -> bool      同上
/// </summary>
public class LuaEnvProxy : IRegisterLua
{
    public string Name { get; } = "env";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["get"] = DynValue.NewCallback((_, args) =>
        {
            var name = ArgName(args, "env.get(name)");
            var v = Environment.GetEnvironmentVariable(name);
            return v is null ? DynValue.Nil : DynValue.NewString(v);
        });

        t["has"] = DynValue.NewCallback((_, args) =>
        {
            var name = ArgName(args, "env.has(name)");
            return DynValue.NewBoolean(Environment.GetEnvironmentVariable(name) is not null);
        });

        t["expand"] = DynValue.NewCallback((_, args) =>
        {
            var str = ArgName(args, "env.expand(str)");
            return DynValue.NewString(Environment.ExpandEnvironmentVariables(str));
        });

        t["list"] = DynValue.NewCallback((_, _) =>
        {
            var tbl = new Table(s);
            foreach (var kv in Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>())
            {
                var k = kv.Key?.ToString();
                var v = kv.Value?.ToString();
                if (k is null || v is null)
                    continue;
                tbl[k] = v;
            }
            return DynValue.NewTable(tbl);
        });

        t["set"] = DynValue.NewCallback((_, args) =>
        {
            if (!MainPlugin.CacheConfig.EnvWriteEnabled)
                throw new ScriptRuntimeException("env.set: 环境变量写入未启用（Config.EnvWriteEnabled = false）");

            var name = ArgName(args, "env.set(name, value)");
            if (args.Count < 2 || args[1].Type != DataType.String)
                throw new ScriptRuntimeException("env.set(name, value): value 需要字符串");

            try
            {
                Environment.SetEnvironmentVariable(name, args[1].String);
                return DynValue.True;
            }
            catch { return DynValue.False; }
        });

        t["delete"] = DynValue.NewCallback((_, args) =>
        {
            if (!MainPlugin.CacheConfig.EnvWriteEnabled)
                throw new ScriptRuntimeException("env.delete: 环境变量写入未启用（Config.EnvWriteEnabled = false）");

            var name = ArgName(args, "env.delete(name)");
            try
            {
                Environment.SetEnvironmentVariable(name, null);
                return DynValue.True;
            }
            catch { return DynValue.False; }
        });

        return t;
    }
    static string ArgName(CallbackArguments a, string sig)
    {
        if (a.Count < 1 || a[0].Type != DataType.String)
            throw new ScriptRuntimeException(sig);
        return a[0].String;
    }
}
