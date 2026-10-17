using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Collections;

/// <summary>
/// 扩展 Lua 原生 table 表（不替换）：
///   keys / values
///   copy / deepcopy / merge / deepMerge
///   contains / hasKey / size / isEmpty
/// </summary>
internal class LuaTableProxy : IRegisterLua
{
    public string Name { get; } = "table";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = s.Globals.Get("table").Table;

        t["keys"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgTable(args, 0, "table.keys(t)");
            var r = new Table(s);
            var i = 1;
            foreach (var p in src.Pairs) r[i++] = p.Key;
            return DynValue.NewTable(r);
        });

        t["values"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgTable(args, 0, "table.values(t)");
            var r = new Table(s);
            var i = 1;
            foreach (var p in src.Pairs) r[i++] = p.Value;
            return DynValue.NewTable(r);
        });

        t["copy"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgTable(args, 0, "table.copy(t)");
            var r = new Table(s);
            foreach (var p in src.Pairs) r.Set(p.Key, p.Value);
            return DynValue.NewTable(r);
        });

        t["deepcopy"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgTable(args, 0, "table.deepcopy(t)");
            return DynValue.NewTable(DeepCopy(s, src, []));
        });

        t["merge"] = DynValue.NewCallback((_, args) =>
        {
            var r = new Table(s);
            for (var i = 0; i < args.Count; i++)
            {
                if (args[i].Type != DataType.Table) continue;
                foreach (var p in args[i].Table.Pairs) r.Set(p.Key, p.Value);
            }
            return DynValue.NewTable(r);
        });

        t["deepMerge"] = DynValue.NewCallback((_, args) =>
        {
            var r = new Table(s);
            for (var i = 0; i < args.Count; i++)
            {
                if (args[i].Type != DataType.Table) continue;
                DeepMerge(s, r, args[i].Table);
            }
            return DynValue.NewTable(r);
        });

        t["contains"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgTable(args, 0, "table.contains(t, value)");
            var val = args.Count > 1 ? args[1] : DynValue.Nil;

            foreach (var p in src.Pairs)
                if (ValuesEqual(p.Value, val)) return DynValue.True;

            return DynValue.False;
        });

        t["hasKey"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgTable(args, 0, "table.hasKey(t, key)");
            return args.Count < 2
                ? DynValue.False
                : DynValue.NewBoolean(src.Get(args[1]).Type != DataType.Nil);
        });

        t["size"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgTable(args, 0, "table.size(t)");
            var n = 0;
            foreach (var __ in src.Pairs) n++;
            return DynValue.NewNumber(n);
        });

        t["isEmpty"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgTable(args, 0, "table.isEmpty(t)");
            foreach (var __ in src.Pairs) return DynValue.False;
            return DynValue.True;
        });

        return t;
    }
    private static Table ArgTable(CallbackArguments args, int i, string sig)
    {
        return args.Count <= i || args[i].Type != DataType.Table
            ? throw new ScriptRuntimeException(sig)
            : args[i].Table;
    }
    private static bool ValuesEqual(DynValue a, DynValue b)
    {
        return a.Type == b.Type && a.Type switch
        {
            DataType.Nil or DataType.Void => true,
            DataType.Boolean => a.Boolean == b.Boolean,
            DataType.Number => a.Number == b.Number,
            DataType.String => a.String == b.String,
            DataType.Table => ReferenceEquals(a.Table, b.Table),
            DataType.Function => ReferenceEquals(a.Function, b.Function),
            _ => false,
        };
    }

    private static Table DeepCopy(Script s, Table src, Dictionary<Table, Table> seen)
    {
        if (seen.TryGetValue(src, out var cached)) return cached;

        var r = new Table(s);
        seen[src] = r;

        foreach (var p in src.Pairs)
        {
            var k = p.Key.Type == DataType.Table
                ? DynValue.NewTable(DeepCopy(s, p.Key.Table, seen))
                : p.Key;

            var v = p.Value.Type == DataType.Table
                ? DynValue.NewTable(DeepCopy(s, p.Value.Table, seen))
                : p.Value;

            r.Set(k, v);
        }
        return r;
    }

    private static void DeepMerge(Script s, Table target, Table source)
    {
        foreach (var p in source.Pairs)
        {
            if (p.Value.Type == DataType.Table)
            {
                var existing = target.Get(p.Key);
                if (existing.Type == DataType.Table)
                {
                    DeepMerge(s, existing.Table, p.Value.Table);
                    continue;
                }

                var fresh = new Table(s);
                DeepMerge(s, fresh, p.Value.Table);
                target.Set(p.Key, DynValue.NewTable(fresh));
                continue;
            }

            target.Set(p.Key, p.Value);
        }
    }
}