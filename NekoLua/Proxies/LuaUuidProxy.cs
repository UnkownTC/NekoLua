using MoonSharp.Interpreter;

using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies;

/// <summary>
/// uuid 表：
///   uuid.new()                      -- "xxxxxxxx-xxxx-..."
///   uuid.newShort()                 -- 无连字符
///   uuid.isValid(str)               -> bool
///   uuid.parse(str)                 -> table | nil
///   uuid.nil                        -- 全零 UUID
/// </summary>
internal class LuaUuidProxy : IRegisterLua
{
    public string Name { get; } = "uuid";
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["new"] = DynValue.NewCallback((_, _) =>
            DynValue.NewString(Guid.NewGuid().ToString("D")));

        t["newShort"] = DynValue.NewCallback((_, _) =>
            DynValue.NewString(Guid.NewGuid().ToString("N")));

        t["isValid"] = DynValue.NewCallback((x, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                return DynValue.False;
            return DynValue.NewBoolean(Guid.TryParse(args[0].String, out _));
        });

        t["parse"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("uuid.parse(str)");

            if (!Guid.TryParse(args[0].String, out var g))
                return DynValue.Nil;

            var bytes = g.ToByteArray();
            var tbl = new Table(s)
            {
                ["value"] = g.ToString("D"),
                ["short"] = g.ToString("N"),
                ["toString"] = g.ToString(),
            };
            return DynValue.NewTable(tbl);
        });

        t["nil"] = Guid.Empty.ToString("D");

        return t;
    }
}
