using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Text.Serialization;

/// <summary>
/// xml 表：
///   xml.decode(str)                 -> table     属性用 "@name"，文本用 "#text"
///   xml.encode(value[, rootName])   -> string
///   xml.isValid(str)                -> bool
/// </summary>
public class LuaXmlProxy : IRegisterLua
{
    public string Name { get; } = "xml";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["decode"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("xml.decode(str)");

            try
            { return LuaXml.FromXml(s, args[0].String); }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"xml.decode: {ex.Message}");
            }
        });

        t["encode"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1)
                throw new ScriptRuntimeException("xml.encode(value[, rootName])");

            var root = args.Count > 1 && args[1].Type == DataType.String
                ? args[1].String : "root";

            try
            { return DynValue.NewString(LuaXml.ToXml(args[0], root)); }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"xml.encode: {ex.Message}");
            }
        });

        t["isValid"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                return DynValue.False;

            try
            {
                System.Xml.Linq.XDocument.Parse(args[0].String);
                return DynValue.True;
            }
            catch { return DynValue.False; }
        });

        return t;
    }
}
