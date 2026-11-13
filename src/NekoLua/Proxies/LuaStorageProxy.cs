using MoonSharp.Interpreter;

using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies;

/// <summary>
/// storage 表（仅内存，跨热重载保留）：
///   storage.set(key, value)
///   storage.get(key)                -> value | nil
///   storage.has(key)                -> bool
///   storage.delete(key)             -> bool
///   storage.clear()
///   storage.keys()                  -> table
///   storage.count()                 -> number
/// </summary>
public class LuaStorageProxy : IRegisterLua
{
    public string Name { get; } = "storage";

    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var store = new Table(script);
        var t = new Table(script);

        t["set"] = DynValue.NewCallback((_, args) =>
        {
            CheckKey(args, "storage.set(key, value)");
            if (args.Count < 2)
                throw new ScriptRuntimeException("storage.set(key, value): 缺少 value");

            store[args[0].String] = args[1];
            return DynValue.Nil;
        });

        t["get"] = DynValue.NewCallback((_, args) =>
        {
            CheckKey(args, "storage.get(key)");
            return store.Get(args[0].String);
        });

        t["has"] = DynValue.NewCallback((_, args) =>
        {
            CheckKey(args, "storage.has(key)");
            var v = store.Get(args[0].String);
            return DynValue.NewBoolean(v.Type != DataType.Nil && v.Type != DataType.Void);
        });

        t["delete"] = DynValue.NewCallback((_, args) =>
        {
            CheckKey(args, "storage.delete(key)");

            var key = args[0].String;
            var had = store.Get(key).Type != DataType.Nil;
            store[key] = DynValue.Nil;
            return DynValue.NewBoolean(had);
        });

        t["clear"] = DynValue.NewCallback((_, _) =>
        {
            var keys = new List<DynValue>();
            foreach (var p in store.Pairs)
                keys.Add(p.Key);

            foreach (var k in keys)
                store[k] = DynValue.Nil;

            return DynValue.Nil;
        });

        t["keys"] = DynValue.NewCallback((_, _) =>
        {
            var result = new Table(script);
            var i = 1;
            foreach (var p in store.Pairs)
                result[i++] = p.Key;
            return DynValue.NewTable(result);
        });

        t["count"] = DynValue.NewCallback((_, _) =>
        {
            var n = 0;
            foreach (var _ in store.Pairs)
                n++;
            return DynValue.NewNumber(n);
        });

        return t;
    }
    static void CheckKey(CallbackArguments args, string sig)
    {
        if (args.Count < 1 || args[0].Type != DataType.String)
            throw new ScriptRuntimeException($"{sig}: key 需要字符串");
    }
}
