using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;
using System.Text;

namespace NekoLua.Proxies.Text;

/// <summary>
/// base64 表：
///   base64.encode / decode(str)     -- 标准 base64
///   base64.urlEncode / urlDecode(str)  -- URL-safe（-_，无 padding）
///   base64.isValid(str)             -> bool
/// </summary>
internal class LuaBase64Proxy : IRegisterLua
{
    public string Name { get; } = "base64";
    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);

        t["encode"] = DynValue.NewCallback((_, args) =>
        {
            var s = ArgString(args, 0, "base64.encode(str)");
            return DynValue.NewString(Convert.ToBase64String(Encoding.UTF8.GetBytes(s)));
        });

        t["decode"] = DynValue.NewCallback((_, args) =>
        {
            var s = ArgString(args, 0, "base64.decode(str)");
            try
            {
                var bytes = Convert.FromBase64String(s);
                return DynValue.NewString(Encoding.UTF8.GetString(bytes));
            }
            catch (FormatException)
            {
                throw new ScriptRuntimeException("base64.decode: 非法 base64 字符串");
            }
        });

        t["urlEncode"] = DynValue.NewCallback((_, args) =>
        {
            var s = ArgString(args, 0, "base64.urlEncode(str)");
            var std = Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
            return DynValue.NewString(ToUrlSafe(std));
        });

        t["urlDecode"] = DynValue.NewCallback((_, args) =>
        {
            var s = ArgString(args, 0, "base64.urlDecode(str)");
            try
            {
                var std = FromUrlSafe(s);
                var bytes = Convert.FromBase64String(std);
                return DynValue.NewString(Encoding.UTF8.GetString(bytes));
            }
            catch (FormatException)
            {
                throw new ScriptRuntimeException("base64.urlDecode: 非法 base64url 字符串");
            }
        });

        t["isValid"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                return DynValue.False;

            try
            {
                Convert.FromBase64String(args[0].String);
                return DynValue.True;
            }
            catch
            {
                return DynValue.False;
            }
        });

        return t;
    }
    static string ArgString(CallbackArguments args, int index, string sig)
    {
        return args.Count <= index || args[index].Type != DataType.String ? throw new ScriptRuntimeException(sig) : args[index].String;
    }
    static string ToUrlSafe(string b64)
    {
        return b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    static string FromUrlSafe(string urlSafe)
    {
        var s = urlSafe.Replace('-', '+').Replace('_', '/');

        switch (s.Length % 4)
        {
            case 2:
                s += "==";
                break;
            case 3:
                s += "=";
                break;
        }

        return s;
    }
}
