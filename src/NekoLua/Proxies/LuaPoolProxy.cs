using MoonSharp.Interpreter;

using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies;

/// <summary>
/// pool 表：
///   local p = pool.new([capacity], factory)
///   p:get()                         -- 取（空则调 factory）
///   p:release(obj)                  -- 放回
///   p:size() / p:clear()
/// </summary>
public class LuaPoolProxy : IRegisterLua
{
    public string Name { get; } = "pool";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["new"] = DynValue.NewCallback((_, args) =>
        {
            int cap;
            DynValue factory;

            if (args.Count >= 2 && args[0].Type == DataType.Number && args[1].Type == DataType.Function)
            {
                cap = (int)args[0].Number;
                factory = args[1];
            }
            else if (args.Count >= 1 && args[0].Type == DataType.Function)
            {
                cap = 0;
                factory = args[0];
            }
            else
            {
                throw new ScriptRuntimeException("pool.new([capacity], factory)");
            }

            if (cap < 0 || cap > 100_000)
                throw new ScriptRuntimeException("pool.new: capacity 应在 0~100000");

            var pool = new List<DynValue>();

            for (var i = 0; i < cap; i++)
            {
                var item = factory.Function.Call();
                if (item.Type != DataType.Nil && item.Type != DataType.Void)
                    pool.Add(item);
            }

            return DynValue.NewTable(BuildPool(s, pool, factory));
        });

        return t;
    }
    private static Table BuildPool(Script script, List<DynValue> pool, DynValue factory)
    {
        var t = new Table(script);
        var meta = new Table(script);

        t["get"] = DynValue.NewCallback((_, _) =>
        {
            if (pool.Count == 0)
            {
                var item = factory.Function.Call();
                return item;
            }

            var last = pool[pool.Count - 1];
            pool.RemoveAt(pool.Count - 1);
            return last;
        });

        t["release"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1)
                return DynValue.False;

            var obj = args[0];
            if (obj.Type == DataType.Nil || obj.Type == DataType.Void)
                return DynValue.False;

            pool.Add(obj);
            return DynValue.True;
        });

        t["size"] = DynValue.NewCallback((_, _) => DynValue.NewNumber(pool.Count));
        t["clear"] = DynValue.NewCallback((_, _) => { pool.Clear(); return DynValue.Nil; });

        meta["__index"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[1].Type != DataType.String)
                return DynValue.Nil;
            return t.Get(args[1].String);
        });

        t.MetaTable = meta;
        return t;
    }
}
