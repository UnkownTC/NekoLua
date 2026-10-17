using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Numerics;

/// <summary>
/// bit 表（32 位整数位运算）：
///   bit.band / bor / bxor(a, b, ...)     -- 折叠
///   bit.bnot(a)
///   bit.lshift / rshift / arshift / rol / ror(a, n)
///   bit.tobit(a)
///   bit.tohex(a[, n])                    -> 十六进制字符串
/// </summary>
internal class LuaBitProxy : IRegisterLua
{
    public string Name { get; } = "bit";
    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);

        t["band"] = Fold((a, b) => a & b, "bit.band(a, b, ...)");
        t["bor"] = Fold((a, b) => a | b, "bit.bor(a, b, ...)");
        t["bxor"] = Fold((a, b) => a ^ b, "bit.bxor(a, b, ...)");
        t["bnot"] = Un(a => ~a, "bit.bnot(a)");
        t["lshift"] = Bin((a, b) => a << (b & 31), "bit.lshift(a, n)");
        t["rshift"] = Bin((a, b) => (int)((uint)a >> (b & 31)), "bit.rshift(a, n)");
        t["arshift"] = Bin((a, b) => a >> (b & 31), "bit.arshift(a, n)");

        t["rol"] = Bin((a, b) =>
        {
            var n = b & 31;
            return (int)(((uint)a << n) | ((uint)a >> (32 - n)));
        }, "bit.rol(a, n)");

        t["ror"] = Bin((a, b) =>
        {
            var n = b & 31;
            return (int)(((uint)a >> n) | ((uint)a << (32 - n)));
        }, "bit.ror(a, n)");

        t["tobit"] = Un(a => a, "bit.tobit(a)");

        t["tohex"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("bit.tohex(a[, n])");

            var a = (int)args[0].Number;
            var n = args.Count > 1 && args[1].Type == DataType.Number
                ? (int)args[1].Number : 8;

            var s = ((uint)a).ToString("x8").Substring(8 - Math.Abs(n));
            return DynValue.NewString(s);
        });

        return t;
    }
    static DynValue Fold(Func<int, int, int> f, string sig)
    {
        return DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2)
                throw new ScriptRuntimeException(sig);

            var acc = (int)args[0].Number;
            for (var i = 1; i < args.Count; i++)
                acc = f(acc, (int)args[i].Number);
            return DynValue.NewNumber(acc);
        });
    }
    static DynValue Bin(Func<int, int, int> f, string sig)
    {
        return DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2)
                throw new ScriptRuntimeException(sig);
            return DynValue.NewNumber(f((int)args[0].Number, (int)args[1].Number));
        });
    }
    static DynValue Un(Func<int, int> f, string sig)
    {
        return DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1)
                throw new ScriptRuntimeException(sig);
            return DynValue.NewNumber(f((int)args[0].Number));
        });
    }
}
