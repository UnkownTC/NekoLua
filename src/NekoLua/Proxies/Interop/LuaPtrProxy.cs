using MoonSharp.Interpreter;

using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Interop;

/// <summary>
/// intptr 表 —— IntPtr 的 Lua 封装（table + 元表）：
///
///   构造：
///     intptr.new(value)               -- 从 int64 构造
///     intptr.parse("0x12345")         -- 从十六进制字符串解析
///     intptr.null                     -- 空指针（值 = 0）
///     intptr.zero                     -- 同 null
///
///   实例字段/方法：
///     p.value                         -- int64
///     p.isNull                        -- bool
///     p:add(n) -> ptr                 -- 加偏移
///     p:sub(n) -> ptr                 -- 减偏移
///     p:equals(other) -> bool         -- 相等比较
///     p:toInt()  -> number            -- 同 value
///     p:toHex()  -> string            -- "0x12345"
///     p:toString() -> string          -- 同 toHex
///     tostring(p)                     -- 通过 __tostring，同 toHex
///
///   相等运算符：
///     p1 == p2                        -- 通过 __eq，比较句柄值
/// </summary>
public class LuaIntPtrProxy : IRegisterLua
{
    public string Name { get; } = "intptr";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["new"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("intptr.new(value): 需要数字");

            return DynValue.NewTable(Build(s, new IntPtr((long)args[0].Number)));
        });

        t["parse"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("intptr.parse(str)");

            var str = args[0].String.Trim();

            try
            {
                long value;

                if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    value = Convert.ToInt64(str.Substring(2), 16);
                }
                else if (long.TryParse(str, out var dec))
                {
                    value = dec;
                }
                else
                {
                    throw new ScriptRuntimeException(
                        $"intptr.parse: 无法解析 '{str}'");
                }

                return DynValue.NewTable(Build(s, new IntPtr(value)));
            }
            catch (ScriptRuntimeException) { throw; }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"intptr.parse: {ex.Message}");
            }
        });

        t["null"] = DynValue.NewTable(Build(s, IntPtr.Zero));
        t["zero"] = t.Get("null");

        return t;
    }
    internal static Table Build(Script script, IntPtr handle)
    {
        var t = new Table(script);
        var meta = new Table(script);

        t["value"] = handle.ToInt64();
        t["isNull"] = handle == IntPtr.Zero;


        meta["__index"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[1].Type != DataType.String)
                return DynValue.Nil;

            return args[1].String switch
            {
                "add" => DynValue.NewCallback((_, a) =>
                {
                    var n = a.Count > 0 && a[0].Type == DataType.Number
                        ? (long)a[0].Number : 0L;

                    return DynValue.NewTable(
                        Build(script, new IntPtr(handle.ToInt64() + n)));
                }),

                "sub" => DynValue.NewCallback((_, a) =>
                {
                    var n = a.Count > 0 && a[0].Type == DataType.Number
                        ? (long)a[0].Number : 0L;

                    return DynValue.NewTable(
                        Build(script, new IntPtr(handle.ToInt64() - n)));
                }),

                "equals" => DynValue.NewCallback((_, a) =>
                {
                    if (a.Count < 1 || a[0].Type != DataType.Table)
                        return DynValue.False;

                    var other = a[0].Table.Get("value");
                    if (other.Type != DataType.Number)
                        return DynValue.False;

                    return DynValue.NewBoolean(
                        handle.ToInt64() == (long)other.Number);
                }),

                "toInt" => DynValue.NewCallback((_, _) => DynValue.NewNumber(handle.ToInt64())),
                "toHex" => DynValue.NewCallback((_, _) => DynValue.NewString(ToHex(handle))),
                "toString" => DynValue.NewCallback((_, _) => DynValue.NewString(ToHex(handle))),

                _ => DynValue.Nil,
            };
        });

        meta["__eq"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 ||
                args[0].Type != DataType.Table ||
                args[1].Type != DataType.Table)
                return DynValue.False;

            var v1 = args[0].Table.Get("value");
            var v2 = args[1].Table.Get("value");

            if (v1.Type != DataType.Number || v2.Type != DataType.Number)
                return DynValue.False;

            return DynValue.NewBoolean(v1.Number == v2.Number);
        });

        meta["__tostring"] = DynValue.NewCallback((_, _) =>
            DynValue.NewString(ToHex(handle)));

        t.MetaTable = meta;
        return t;
    }
    private static string ToHex(IntPtr handle)
        => "0x" + handle.ToInt64().ToString("x");
}
