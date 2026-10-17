using MoonSharp.Interpreter;
using NekoLua.Api;
using NekoLua.Loader;
using NekoLua.Registers;

using UnityEngine;

namespace NekoLua.Proxies.Numerics;

/// <summary>
/// quaternion 表：
///   quaternion.new(x, y, z, w) / identity
///   构造：euler(x,y,z) / euler(vec3) / fromAngleAxis / lookRotation / fromToRotation
///   运算：multiply / inverse
///   插值：slerp / lerp
///   查询：rotate(q,v) / angle / dot / eulerAngles
///   分量：x / y / z / w(q)
/// </summary>
internal class LuaQuaternionProxy : IRegisterLua
{
    public string Name { get; } = "quaternion";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["new"] = DynValue.NewCallback((_, a) =>
        {
            return a.Count < 4
                ? throw new ScriptRuntimeException("quaternion.new(x, y, z, w)")
                : DynValue.NewTable(LuaVector.PackQuaternion(s, new Quaternion(
                (float)a[0].Number,
                (float)a[1].Number,
                (float)a[2].Number,
                (float)a[3].Number)));
        });

        t["identity"] = DynValue.NewTable(LuaVector.PackQuaternion(s, Quaternion.identity));

        t["euler"] = DynValue.NewCallback((_, a) =>
        {
            Vector3 e = a.Count >= 3 && a[0].Type == DataType.Number
                ? new Vector3((float)a[0].Number, (float)a[1].Number, (float)a[2].Number)
                : LuaVector.UnpackVector3(a, 0, "quaternion.euler(x, y, z)");
            return DynValue.NewTable(LuaVector.PackQuaternion(s, Quaternion.Euler(e)));
        });

        t["fromAngleAxis"] = DynValue.NewCallback((_, a) =>
        {
            if (a.Count < 2 || a[0].Type != DataType.Number)
                throw new ScriptRuntimeException("quaternion.fromAngleAxis(angle, axis)");

            var angle = (float)a[0].Number;
            var axis = LuaVector.UnpackVector3(a, 1, "quaternion.fromAngleAxis(angle, axis)");
            return DynValue.NewTable(LuaVector.PackQuaternion(s, Quaternion.AngleAxis(angle, axis)));
        });

        t["lookRotation"] = DynValue.NewCallback((_, a) =>
        {
            var fwd = LuaVector.UnpackVector3(a, 0, "quaternion.lookRotation(forward[, up])");
            var up = a.Count >= 2
                ? LuaVector.UnpackVector3(a, 1, "quaternion.lookRotation(forward[, up])")
                : Vector3.up;

            return DynValue.NewTable(LuaVector.PackQuaternion(s, Quaternion.LookRotation(fwd, up)));
        });

        t["fromToRotation"] = DynValue.NewCallback((_, a) =>
        {
            var from = LuaVector.UnpackVector3(a, 0, "quaternion.fromToRotation(from, to)");
            var to = LuaVector.UnpackVector3(a, 1, "quaternion.fromToRotation(from, to)");
            return DynValue.NewTable(LuaVector.PackQuaternion(s, Quaternion.FromToRotation(from, to)));
        });

        t["multiply"] = DynValue.NewCallback((_, a) =>
        {
            var x = LuaVector.UnpackQuaternion(a, 0, "quaternion.multiply(a, b)");
            var y = LuaVector.UnpackQuaternion(a, 1, "quaternion.multiply(a, b)");
            return DynValue.NewTable(LuaVector.PackQuaternion(s, x * y));
        });

        t["inverse"] = DynValue.NewCallback((_, a) =>
        {
            var q = LuaVector.UnpackQuaternion(a, 0, "quaternion.inverse(q)");
            return DynValue.NewTable(LuaVector.PackQuaternion(s, Quaternion.Inverse(q)));
        });

        t["slerp"] = DynValue.NewCallback((_, a) =>
        {
            var x = LuaVector.UnpackQuaternion(a, 0, "quaternion.slerp(a, b, t)");
            var y = LuaVector.UnpackQuaternion(a, 1, "quaternion.slerp(a, b, t)");
            if (a.Count < 3 || a[2].Type != DataType.Number)
                throw new ScriptRuntimeException("quaternion.slerp(a, b, t)");

            var k = Mathf.Clamp01((float)a[2].Number);
            return DynValue.NewTable(LuaVector.PackQuaternion(s, Quaternion.Slerp(x, y, k)));
        });

        t["lerp"] = DynValue.NewCallback((_, a) =>
        {
            var x = LuaVector.UnpackQuaternion(a, 0, "quaternion.lerp(a, b, t)");
            var y = LuaVector.UnpackQuaternion(a, 1, "quaternion.lerp(a, b, t)");
            if (a.Count < 3 || a[2].Type != DataType.Number)
                throw new ScriptRuntimeException("quaternion.lerp(a, b, t)");

            var k = Mathf.Clamp01((float)a[2].Number);
            return DynValue.NewTable(LuaVector.PackQuaternion(s, Quaternion.Lerp(x, y, k)));
        });

        t["rotate"] = DynValue.NewCallback((_, a) =>
        {
            var q = LuaVector.UnpackQuaternion(a, 0, "quaternion.rotate(q, v)");
            var v = LuaVector.UnpackVector3(a, 1, "quaternion.rotate(q, v)");
            return DynValue.NewTable(LuaVector.PackVector3(s, q * v));
        });

        t["angle"] = DynValue.NewCallback((_, a) =>
        {
            var x = LuaVector.UnpackQuaternion(a, 0, "quaternion.angle(a, b)");
            var y = LuaVector.UnpackQuaternion(a, 1, "quaternion.angle(a, b)");
            return DynValue.NewNumber(Quaternion.Angle(x, y));
        });

        t["dot"] = DynValue.NewCallback((_, a) =>
        {
            var x = LuaVector.UnpackQuaternion(a, 0, "quaternion.dot(a, b)");
            var y = LuaVector.UnpackQuaternion(a, 1, "quaternion.dot(a, b)");
            return DynValue.NewNumber(Quaternion.Dot(x, y));
        });

        t["eulerAngles"] = DynValue.NewCallback((_, a) =>
        {
            var q = LuaVector.UnpackQuaternion(a, 0, "quaternion.eulerAngles(q)");
            return DynValue.NewTable(LuaVector.PackVector3(s, q.eulerAngles));
        });

        t["x"] = Num(a => LuaVector.UnpackQuaternion(a, 0, "quaternion.x(q)").x);
        t["y"] = Num(a => LuaVector.UnpackQuaternion(a, 0, "quaternion.y(q)").y);
        t["z"] = Num(a => LuaVector.UnpackQuaternion(a, 0, "quaternion.z(q)").z);
        t["w"] = Num(a => LuaVector.UnpackQuaternion(a, 0, "quaternion.w(q)").w);

        return t;
    }

    static DynValue Num(Func<CallbackArguments, float> f)
    {
        return DynValue.NewCallback((_, a) => DynValue.NewNumber(f(a)));
    }
}
