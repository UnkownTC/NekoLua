using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using UnityEngine;

namespace NekoLua.Proxies.Numerics;

/// <summary>
/// vector2 表：
///   vector2.new(x, y)
///   常量：zero / one / up / down / left / right
///   运算：add / sub / mul / div / neg
///   几何：dot / length / lengthSqr / normalize / distance / angle / lerp
///   分量：x(v) / y(v)
/// </summary>
public class LuaVector2Proxy : IRegisterLua
{
    public string Name { get; } = "vector2";
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["new"] = DynValue.NewCallback((_, a) =>
        {
            if (a.Count < 2)
                throw new ScriptRuntimeException("vector2.new(x, y)");

            return DynValue.NewTable(LuaVector.PackV2(s, new Vector2(
                (float)a[0].Number,
                (float)a[1].Number)));
        });

        t["zero"] = DynValue.NewTable(LuaVector.PackV2(s, Vector2.zero));
        t["one"] = DynValue.NewTable(LuaVector.PackV2(s, Vector2.one));
        t["up"] = DynValue.NewTable(LuaVector.PackV2(s, Vector2.up));
        t["down"] = DynValue.NewTable(LuaVector.PackV2(s, Vector2.down));
        t["left"] = DynValue.NewTable(LuaVector.PackV2(s, Vector2.left));
        t["right"] = DynValue.NewTable(LuaVector.PackV2(s, Vector2.right));

        t["add"] = Bin(s, (x, y) => x + y, "vector2.add(a, b)");
        t["sub"] = Bin(s, (x, y) => x - y, "vector2.sub(a, b)");
        t["neg"] = Un(s, v => -v, "vector2.neg(a)");

        t["mul"] = DynValue.NewCallback((_, a) =>
        {
            var v = LuaVector.UnpackV2(a, 0, "vector2.mul(a, k)");
            if (a.Count < 2 || a[1].Type != DataType.Number)
                throw new ScriptRuntimeException("vector2.mul(a, k)");

            return DynValue.NewTable(LuaVector.PackV2(s, v * (float)a[1].Number));
        });

        t["div"] = DynValue.NewCallback((_, a) =>
        {
            var v = LuaVector.UnpackV2(a, 0, "vector2.div(a, k)");
            if (a.Count < 2 || a[1].Type != DataType.Number)
                throw new ScriptRuntimeException("vector2.div(a, k)");

            var k = (float)a[1].Number;
            if (k == 0)
                throw new ScriptRuntimeException("vector2.div: 除数为 0");

            return DynValue.NewTable(LuaVector.PackV2(s, v / k));
        });

        t["dot"] = DynValue.NewCallback((_, a) =>
        {
            var x = LuaVector.UnpackV2(a, 0, "vector2.dot(a, b)");
            var y = LuaVector.UnpackV2(a, 1, "vector2.dot(a, b)");
            return DynValue.NewNumber(Vector2.Dot(x, y));
        });

        t["length"] = Num(a => LuaVector.UnpackV2(a, 0, "vector2.length(v)").magnitude);
        t["lengthSqr"] = Num(a => LuaVector.UnpackV2(a, 0, "vector2.lengthSqr(v)").sqrMagnitude);

        t["normalize"] = Un(s, v => v.normalized, "vector2.normalize(v)");

        t["distance"] = Num(a =>
            Vector2.Distance(
                LuaVector.UnpackV2(a, 0, "vector2.distance(a, b)"),
                LuaVector.UnpackV2(a, 1, "vector2.distance(a, b)")));

        t["angle"] = Num(a =>
            Vector2.Angle(
                LuaVector.UnpackV2(a, 0, "vector2.angle(a, b)"),
                LuaVector.UnpackV2(a, 1, "vector2.angle(a, b)")));

        t["lerp"] = DynValue.NewCallback((_, a) =>
        {
            var x = LuaVector.UnpackV2(a, 0, "vector2.lerp(a, b, t)");
            var y = LuaVector.UnpackV2(a, 1, "vector2.lerp(a, b, t)");
            if (a.Count < 3 || a[2].Type != DataType.Number)
                throw new ScriptRuntimeException("vector2.lerp(a, b, t)");

            var k = Mathf.Clamp01((float)a[2].Number);
            return DynValue.NewTable(LuaVector.PackV2(s, Vector2.Lerp(x, y, k)));
        });

        t["x"] = Num(a => LuaVector.UnpackV2(a, 0, "vector2.x(v)").x);
        t["y"] = Num(a => LuaVector.UnpackV2(a, 0, "vector2.y(v)").y);

        return t;
    }

    static DynValue Num(Func<CallbackArguments, float> f)
        => DynValue.NewCallback((_, a) => DynValue.NewNumber(f(a)));

    static DynValue Bin(Script s, Func<Vector2, Vector2, Vector2> op, string sig)
        => DynValue.NewCallback((_, a) =>
        {
            var x = LuaVector.UnpackV2(a, 0, sig);
            var y = LuaVector.UnpackV2(a, 1, sig);
            return DynValue.NewTable(LuaVector.PackV2(s, op(x, y)));
        });

    static DynValue Un(Script s, Func<Vector2, Vector2> op, string sig)
        => DynValue.NewCallback((_, a) =>
        {
            var v = LuaVector.UnpackV2(a, 0, sig);
            return DynValue.NewTable(LuaVector.PackV2(s, op(v)));
        });
}
