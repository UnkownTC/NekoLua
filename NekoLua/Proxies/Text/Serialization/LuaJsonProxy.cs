using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Text.Serialization;

/// <summary>
/// json 表：
///   json.decode(str)                -> value
///   json.encode(value)              -> string
///   json.isValid(str)               -> bool
/// </summary>
public class LuaJsonProxy : IRegisterLua
{
    public string Name { get; } = "json";
    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var t = new Table(script);

        t["decode"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("json.decode(str): 需要字符串");

            try
            {
                return LuaJson.FromJson(script, args[0].String);
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"json.decode: {ex.Message}");
            }
        });

        t["encode"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1)
                throw new ScriptRuntimeException("json.encode(value): 需要参数");

            try
            {
                return DynValue.NewString(LuaJson.ToJson(args[0]));
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"json.encode: {ex.Message}");
            }
        });

        t["isValid"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                return DynValue.False;

            try
            {
                Newtonsoft.Json.Linq.JToken.Parse(args[0].String);
                return DynValue.True;
            }
            catch
            {
                return DynValue.False;
            }
        });

        return t;
    }
}
