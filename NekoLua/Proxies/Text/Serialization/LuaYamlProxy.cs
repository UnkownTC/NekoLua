using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Text.Serialization;

/// <summary>
/// yaml 表：
///   yaml.decode(str)                -> table
///   yaml.encode(value)              -> string
///   yaml.isValid(str)               -> bool
/// </summary>
public class LuaYamlProxy : IRegisterLua
{
    public string Name { get; } = "yaml";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["decode"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("yaml.decode(str)");

            try
            { return LuaYaml.FromYaml(s, args[0].String); }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"yaml.decode: {ex.Message}");
            }
        });

        t["encode"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1)
                throw new ScriptRuntimeException("yaml.encode(value)");

            try
            { return DynValue.NewString(LuaYaml.ToYaml(args[0])); }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"yaml.encode: {ex.Message}");
            }
        });

        t["isValid"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                return DynValue.False;

            try
            { LuaYaml.FromYaml(s, args[0].String); return DynValue.True; }
            catch { return DynValue.False; }
        });

        return t;
    }
}
