using MoonSharp.Interpreter;

using UnityEngine;

namespace NekoLua.Dependencies;

public static class LuaVector
{

    public static Vector2 UnpackV2Args(DynValue v, string key)
    {
        if (v.Type != DataType.Table)
            throw new ScriptRuntimeException($"{key}: 需要 vector2");

        var t = v.Table;
        return new Vector2(
            (float)t.Get("x").Number,
            (float)t.Get("y").Number);
    }
    public static Vector3 UnpackV3Args(DynValue v, string key)
    {
        if (v.Type != DataType.Table)
            throw new ScriptRuntimeException($"{key}: 需要 vector3");

        var t = v.Table;
        return new Vector3(
            (float)t.Get("x").Number,
            (float)t.Get("y").Number,
            (float)t.Get("z").Number);
    }
    public static Quaternion UnpackQArgs(DynValue v, string key)
    {
        if (v.Type != DataType.Table)
            throw new ScriptRuntimeException($"{key}: 需要 quaternion");

        var t = v.Table;
        return new Quaternion(
            (float)t.Get("x").Number,
            (float)t.Get("y").Number,
            (float)t.Get("z").Number,
            (float)t.Get("w").Number);
    }
    public static Table PackV2(Script script, Vector2 v) => new(script)
    {
        ["x"] = v.x,
        ["y"] = v.y,
    };
    public static Vector2 UnpackV2(CallbackArguments args, int index, string sig)
    {
        if (args.Count <= index || args[index].Type != DataType.Table)
            throw new ScriptRuntimeException($"{sig}: 参数需要 vector2");

        var t = args[index].Table;
        return new Vector2(
            (float)t.Get("x").Number,
            (float)t.Get("y").Number);
    }
    public static Table PackV3(Script script, Vector3 v) => new(script)
    {
        ["x"] = v.x,
        ["y"] = v.y,
        ["z"] = v.z,
    };
    public static Vector3 UnpackV3(CallbackArguments args, int index, string sig)
    {
        if (args.Count <= index || args[index].Type != DataType.Table)
            throw new ScriptRuntimeException($"{sig}: 参数需要 vector3");

        var t = args[index].Table;
        return new Vector3(
            (float)t.Get("x").Number,
            (float)t.Get("y").Number,
            (float)t.Get("z").Number);
    }
    public static Table PackQ(Script script, Quaternion q) => new(script)
    {
        ["x"] = q.x,
        ["y"] = q.y,
        ["z"] = q.z,
        ["w"] = q.w,
    };
    public static Quaternion UnpackQ(CallbackArguments args, int index, string sig)
    {
        if (args.Count <= index || args[index].Type != DataType.Table)
            throw new ScriptRuntimeException($"{sig}: 参数需要 quaternion");

        var t = args[index].Table;
        return new Quaternion(
            (float)t.Get("x").Number,
            (float)t.Get("y").Number,
            (float)t.Get("z").Number,
            (float)t.Get("w").Number);
    }
    public static Table PackBounds(Script script, Bounds b) => new(script)
    {
        ["center"] = DynValue.NewTable(PackV3(script, b.center)),
        ["size"] = DynValue.NewTable(PackV3(script, b.size)),
        ["extents"] = DynValue.NewTable(PackV3(script, b.extents)),
        ["min"] = DynValue.NewTable(PackV3(script, b.min)),
        ["max"] = DynValue.NewTable(PackV3(script, b.max)),
    };

    public static Bounds UnpackBoundsArgs(DynValue v, string key)
    {
        if (v.Type != DataType.Table)
            throw new ScriptRuntimeException($"{key}: 需要 bounds");

        var t = v.Table;
        var center = t.Get("center");
        var size = t.Get("size");

        if (center.Type != DataType.Table || size.Type != DataType.Table)
            throw new ScriptRuntimeException($"{key}: bounds 需要 center 和 size");

        return new Bounds(
            UnpackV3Args(center, key),
            UnpackV3Args(size, key));
    }
    public static Table PackColor(Script script, Color c) => new(script)
    {
        ["r"] = c.r,
        ["g"] = c.g,
        ["b"] = c.b,
        ["a"] = c.a,
    };

    public static Color UnpackColorArgs(DynValue v, string key)
    {
        if (v.Type != DataType.Table)
            throw new ScriptRuntimeException($"{key}: 需要 color");

        var t = v.Table;
        return new Color(
            (float)t.Get("r").Number,
            (float)t.Get("g").Number,
            (float)t.Get("b").Number,
            t.Get("a").Type == DataType.Nil ? 1f : (float)t.Get("a").Number);
    }
}
