using MoonSharp.Interpreter;
using NekoLua.Api;
using NekoLua.Loader;
using NekoLua.Registers;

using UnityEngine;

namespace NekoLua.Proxies.Numerics;

/// <summary>
/// vector3 表：
///   vector3.new(x, y, z)
///   常量：zero / one / up / down / left / right / forward / back
///   运算：add / sub / mul / div / neg
///   几何：dot / cross / length / lengthSqr / normalize / distance / angle / lerp
///         clampMagnitude / project / reflect
///   分量：x(v) / y(v) / z(v)
/// </summary>
internal class LuaVector3Proxy : IRegisterLua
{
    public string Name { get; } = "vector3";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["new"] = DynValue.NewCallback((_, a) =>
        {
            return a.Count < 3
                ? throw new ScriptRuntimeException("vector3.new(x, y, z)")
                : DynValue.NewTable(LuaVector.PackVector3(s, new Vector3(
                (float)a[0].Number,
                (float)a[1].Number,
                (float)a[2].Number)));
        });

        t["zero"] = DynValue.NewTable(LuaVector.PackVector3(s, Vector3.zero));
        t["one"] = DynValue.NewTable(LuaVector.PackVector3(s, Vector3.one));
        t["up"] = DynValue.NewTable(LuaVector.PackVector3(s, Vector3.up));
        t["down"] = DynValue.NewTable(LuaVector.PackVector3(s, Vector3.down));
        t["left"] = DynValue.NewTable(LuaVector.PackVector3(s, Vector3.left));
        t["right"] = DynValue.NewTable(LuaVector.PackVector3(s, Vector3.right));
        t["forward"] = DynValue.NewTable(LuaVector.PackVector3(s, Vector3.forward));
        t["back"] = DynValue.NewTable(LuaVector.PackVector3(s, Vector3.back));

        t["add"] = Bin(s, (x, y) => x + y, "vector3.add(a, b)");
        t["sub"] = Bin(s, (x, y) => x - y, "vector3.sub(a, b)");
        t["neg"] = Un(s, v => -v, "vector3.neg(a)");

        t["mul"] = DynValue.NewCallback((_, a) =>
        {
            var v = LuaVector.UnpackVector3(a, 0, "vector3.mul(a, k)");
            return a.Count < 2 || a[1].Type != DataType.Number
                ? throw new ScriptRuntimeException("vector3.mul(a, k)")
                : DynValue.NewTable(LuaVector.PackVector3(s, v * (float)a[1].Number));
        });

        t["div"] = DynValue.NewCallback((_, a) =>
        {
            var v = LuaVector.UnpackVector3(a, 0, "vector3.div(a, k)");
            if (a.Count < 2 || a[1].Type != DataType.Number)
                throw new ScriptRuntimeException("vector3.div(a, k)");

            var k = (float)a[1].Number;
            return k == 0 ? throw new ScriptRuntimeException("vector3.div: 除数为 0") : DynValue.NewTable(LuaVector.PackVector3(s, v / k));
        });

        t["dot"] = Num(a =>
            Vector3.Dot(
                LuaVector.UnpackVector3(a, 0, "vector3.dot(a, b)"),
                LuaVector.UnpackVector3(a, 1, "vector3.dot(a, b)")));

        t["cross"] = DynValue.NewCallback((_, a) =>
        {
            var x = LuaVector.UnpackVector3(a, 0, "vector3.cross(a, b)");
            var y = LuaVector.UnpackVector3(a, 1, "vector3.cross(a, b)");
            return DynValue.NewTable(LuaVector.PackVector3(s, Vector3.Cross(x, y)));
        });

        t["length"] = Num(a => LuaVector.UnpackVector3(a, 0, "vector3.length(v)").magnitude);
        t["lengthSqr"] = Num(a => LuaVector.UnpackVector3(a, 0, "vector3.lengthSqr(v)").sqrMagnitude);

        t["normalize"] = Un(s, v => v.normalized, "vector3.normalize(v)");

        t["distance"] = Num(a =>
            Vector3.Distance(
                LuaVector.UnpackVector3(a, 0, "vector3.distance(a, b)"),
                LuaVector.UnpackVector3(a, 1, "vector3.distance(a, b)")));

        t["angle"] = Num(a =>
            Vector3.Angle(
                LuaVector.UnpackVector3(a, 0, "vector3.angle(a, b)"),
                LuaVector.UnpackVector3(a, 1, "vector3.angle(a, b)")));

        t["lerp"] = DynValue.NewCallback((_, a) =>
        {
            var x = LuaVector.UnpackVector3(a, 0, "vector3.lerp(a, b, t)");
            var y = LuaVector.UnpackVector3(a, 1, "vector3.lerp(a, b, t)");
            if (a.Count < 3 || a[2].Type != DataType.Number)
                throw new ScriptRuntimeException("vector3.lerp(a, b, t)");

            var k = Mathf.Clamp01((float)a[2].Number);
            return DynValue.NewTable(LuaVector.PackVector3(s, Vector3.Lerp(x, y, k)));
        });

        t["clampMagnitude"] = DynValue.NewCallback((_, a) =>
        {
            var v = LuaVector.UnpackVector3(a, 0, "vector3.clampMagnitude(v, max)");
            return a.Count < 2 || a[1].Type != DataType.Number
                ? throw new ScriptRuntimeException("vector3.clampMagnitude(v, max)")
                : DynValue.NewTable(LuaVector.PackVector3(s, Vector3.ClampMagnitude(v, (float)a[1].Number)));
        });

        t["project"] = DynValue.NewCallback((_, a) =>
        {
            var v = LuaVector.UnpackVector3(a, 0, "vector3.project(v, onNormal)");
            var n = LuaVector.UnpackVector3(a, 1, "vector3.project(v, onNormal)");
            return DynValue.NewTable(LuaVector.PackVector3(s, Vector3.Project(v, n)));
        });

        t["reflect"] = DynValue.NewCallback((_, a) =>
        {
            var v = LuaVector.UnpackVector3(a, 0, "vector3.reflect(inDir, normal)");
            var n = LuaVector.UnpackVector3(a, 1, "vector3.reflect(inDir, normal)");
            return DynValue.NewTable(LuaVector.PackVector3(s, Vector3.Reflect(v, n)));
        });

        t["x"] = Num(a => LuaVector.UnpackVector3(a, 0, "vector3.x(v)").x);
        t["y"] = Num(a => LuaVector.UnpackVector3(a, 0, "vector3.y(v)").y);
        t["z"] = Num(a => LuaVector.UnpackVector3(a, 0, "vector3.z(v)").z);

        return t;
    }
    static DynValue Num(Func<CallbackArguments, float> f)
    {
        return DynValue.NewCallback((_, a) => DynValue.NewNumber(f(a)));
    }

    static DynValue Bin(Script s, Func<Vector3, Vector3, Vector3> op, string sig)
    {
        return DynValue.NewCallback((_, a) =>
            {
                var x = LuaVector.UnpackVector3(a, 0, sig);
                var y = LuaVector.UnpackVector3(a, 1, sig);
                return DynValue.NewTable(LuaVector.PackVector3(s, op(x, y)));
            });
    }

    static DynValue Un(Script s, Func<Vector3, Vector3> op, string sig)
    {
        return DynValue.NewCallback((_, a) =>
            {
                var v = LuaVector.UnpackVector3(a, 0, sig);
                return DynValue.NewTable(LuaVector.PackVector3(s, op(v)));
            });
    }
}
