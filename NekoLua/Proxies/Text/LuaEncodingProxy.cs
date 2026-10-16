using System.Net;
using System.Text;

using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Text;

/// <summary>
/// encoding 表：
///   encoding.hex.encode / decode(str)
///   encoding.url.encode / decode(str)
///   encoding.html.escape / unescape(str)
///   encoding.utf8.length(str)             -- 字节数
///   encoding.utf8.char(codepoint)         -- 单字符
///   encoding.utf8.codepoint(str[, i])     -- 第 i 个字符的 codepoint（1-based）
///   encoding.utf8.bytes(str)              -- 字节值数组
/// </summary>
public class LuaEncodingProxy : IRegisterLua
{
    public string Name { get; } = "encoding";
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        var hex = new Table(s)
        {
            ["encode"] = DynValue.NewCallback((_, a) =>
            {
                var str = Arg(a, 0, "encoding.hex.encode(str)");
                return DynValue.NewString(ToHex(Encoding.UTF8.GetBytes(str)));
            }),
            ["decode"] = DynValue.NewCallback((_, a) =>
            {
                var str = Arg(a, 0, "encoding.hex.decode(str)");
                try
                { return DynValue.NewString(Encoding.UTF8.GetString(FromHex(str))); }
                catch (Exception ex) { throw new ScriptRuntimeException($"encoding.hex.decode: {ex.Message}"); }
            }),
        };
        t["hex"] = DynValue.NewTable(hex);

        var url = new Table(s)
        {
            ["encode"] = DynValue.NewCallback((_, a) =>
                DynValue.NewString(Uri.EscapeDataString(Arg(a, 0, "encoding.url.encode(str)")))),
            ["decode"] = DynValue.NewCallback((_, a) =>
                DynValue.NewString(Uri.UnescapeDataString(Arg(a, 0, "encoding.url.decode(str)")))),
        };
        t["url"] = DynValue.NewTable(url);

        var html = new Table(s)
        {
            ["escape"] = DynValue.NewCallback((_, a) =>
                DynValue.NewString(WebUtility.HtmlEncode(Arg(a, 0, "encoding.html.escape(str)")))),
            ["unescape"] = DynValue.NewCallback((_, a) =>
                DynValue.NewString(WebUtility.HtmlDecode(Arg(a, 0, "encoding.html.unescape(str)")))),
        };
        t["html"] = DynValue.NewTable(html);

        var utf8 = new Table(s)
        {
            ["length"] = DynValue.NewCallback((_, a) =>
                DynValue.NewNumber(Encoding.UTF8.GetByteCount(Arg(a, 0, "encoding.utf8.length(str)")))),

            ["char"] = DynValue.NewCallback((_, a) =>
            {
                if (a.Count < 1 || a[0].Type != DataType.Number)
                    throw new ScriptRuntimeException("encoding.utf8.char(codepoint)");
                try
                { return DynValue.NewString(char.ConvertFromUtf32((int)a[0].Number)); }
                catch { throw new ScriptRuntimeException("encoding.utf8.char: 非法 codepoint"); }
            }),

            ["codepoint"] = DynValue.NewCallback((_, a) =>
            {
                var str = Arg(a, 0, "encoding.utf8.codepoint(str[, i])");
                var i = a.Count > 1 && a[1].Type == DataType.Number ? (int)a[1].Number : 1;
                if (i < 1 || i > str.Length)
                    throw new ScriptRuntimeException($"encoding.utf8.codepoint: 索引越界 {i}");
                return DynValue.NewNumber(char.ConvertToUtf32(str, i - 1));
            }),

            ["bytes"] = DynValue.NewCallback((_, a) =>
            {
                var str = Arg(a, 0, "encoding.utf8.bytes(str)");
                var bytes = Encoding.UTF8.GetBytes(str);
                var tbl = new Table(s);
                for (var i = 0; i < bytes.Length; i++)
                    tbl[i + 1] = bytes[i];
                return DynValue.NewTable(tbl);
            }),
        };
        t["utf8"] = DynValue.NewTable(utf8);

        return t;
    }
    static string Arg(CallbackArguments a, int i, string sig)
    {
        if (a.Count <= i || a[i].Type != DataType.String)
            throw new ScriptRuntimeException(sig);
        return a[i].String;
    }
    static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
    static byte[] FromHex(string hex)
    {
        if (hex.Length % 2 != 0)
            throw new FormatException("hex 长度必须是偶数");

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }
}
