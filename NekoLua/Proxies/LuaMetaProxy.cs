using MoonSharp.Interpreter;
using NekoLua.Api;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies;

/// <summary>
/// meta 表：
///   meta.type(x)                    -- "nil"/"number"/"string"/"table"/...
///   meta.typename(x)                -- 同 type
///   meta.fields(t)                  -> table     键列表
///   meta.hasField(t, k)             -> bool
///   meta.size(t)                    -> number
///   meta.isEmpty(t)                 -> bool
///   meta.deepCopy(x)                -- JSON 往返深拷贝
///   meta.equals(a, b)               -> bool      JSON 深度比较
/// </summary>
internal class LuaMetaProxy : IRegisterLua
{
    public string Name { get; } = "meta";
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["type"] = DynValue.NewCallback((_, args) =>
            DynValue.NewString(args.Count > 0 ? args[0].Type.ToLuaTypeString() : "nil"));

        t["typename"] = t.Get("type");

        t["fields"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Table)
                throw new ScriptRuntimeException("meta.fields(t)");

            var tbl = new Table(s);
            var i = 1;
            foreach (var p in args[0].Table.Pairs)
                tbl[i++] = p.Key;

            return DynValue.NewTable(tbl);
        });

        t["hasField"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.Table)
                throw new ScriptRuntimeException("meta.hasField(t, k)");

            var v = args[0].Table.Get(args[1]);
            return DynValue.NewBoolean(v.Type is not DataType.Nil and not DataType.Void);
        });

        t["size"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Table)
                throw new ScriptRuntimeException("meta.size(t)");

            var n = 0;
            foreach (var s in args[0].Table.Pairs)
                n++;
            return DynValue.NewNumber(n);
        });

        t["isEmpty"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Table)
                return DynValue.True;

            foreach (var s in args[0].Table.Pairs)
                return DynValue.False;

            return DynValue.True;
        });

        t["deepCopy"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1)
                throw new ScriptRuntimeException("meta.deepCopy(x)");

            try
            {
                var json = LuaJson.ToJson(args[0]);
                return LuaJson.FromJson(s, json);
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"meta.deepCopy: {ex.Message}");
            }
        });

        t["equals"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2)
                throw new ScriptRuntimeException("meta.equals(a, b)");

            try
            {
                var a = LuaJson.ToJson(args[0]);
                var b = LuaJson.ToJson(args[1]);
                return DynValue.NewBoolean(a == b);
            }
            catch
            {
                return DynValue.False;
            }
        });

        return t;
    }
}
