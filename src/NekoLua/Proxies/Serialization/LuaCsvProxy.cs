using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Serialization;

/// <summary>
/// csv 表：
///   csv.decode(text[, hasHeader])   -> table
///     hasHeader=true（默认）：[{col=val}, ...]
///     hasHeader=false      ：{{"a", "b"}, ...}
///   csv.encode(table)               -> string
/// </summary>
public class LuaCsvProxy : IRegisterLua
{
    public string Name { get; } = "csv";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["decode"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("csv.decode(text[, hasHeader])");

            var hasHeader = args.Count <= 1 || args[1].Type != DataType.Boolean || args[1].Boolean;

            try
            { return LuaCsv.FromCsv(s, args[0].String, hasHeader); }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"csv.decode: {ex.Message}");
            }
        });

        t["encode"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1)
                throw new ScriptRuntimeException("csv.encode(table)");

            try
            { return DynValue.NewString(LuaCsv.ToCsv(args[0])); }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"csv.encode: {ex.Message}");
            }
        });

        return t;
    }
}
