using MoonSharp.Interpreter;
using UnityEngine;

namespace NekoLua.Api;

/// <summary>
/// 在 Lua（DynValue/Table）和 Unity 数学类型（Vector2、Vector3、Quaternion、Bounds、Color）之间提供打包与解包的静态辅助方法。
/// </summary>
/// <remarks>当传入的 DynValue 或 CallbackArguments 参数不是 Table 时，相关方法会抛出 ScriptRuntimeException。解包方法假定表包含特定字段（例如
/// x、y、z、w 或 r、g、b、a）且这些字段可转换为数值；Pack 方法返回与指定 Script 关联的 Table。</remarks>
public static class LuaVector
{
    /// <summary>
    /// 解析 DynValue 表为 Vector2，从字段 x 和 y 提取浮点分量。
    /// </summary>
    /// <remarks>假定表的 x 和 y 字段存在且可转换为数字；否则访问或转换可能导致运行时异常。</remarks>
    /// <param name="v">要解析为 Vector2 的 DynValue，应为 Table 类型并包含可转换为数字的 x 和 y 字段。</param>
    /// <param name="key">用于错误消息的键或上下文标识符，当 v 不是 Table 类型时包含于抛出的 ScriptRuntimeException 中。</param>
    /// <returns>由表中 x 和 y 字段构造的 Vector2。</returns>
    /// <exception cref="ScriptRuntimeException">当 v 不是 Table 类型时抛出，消息包含 key 和“需要 vector2”的提示。</exception>
    public static Vector2 UnpackVector2Args(DynValue v, string key)
    {
        if (v.Type != DataType.Table)
            throw new ScriptRuntimeException($"{key}: 需要 vector2");

        var t = v.Table;
        return new Vector2(
            (float)t.Get("x").Number,
            (float)t.Get("y").Number);
    }
    /// <summary>
    /// 从表示 Vector3 的 Lua 表解包并返回 Vector3。
    /// </summary>
    /// <param name="v">表示包含 x、y、z 字段的 DynValue（应为表）。</param>
    /// <param name="key">用于构建错误消息的键名，当 v 不是表时包含在异常消息中。</param>
    /// <returns>由表的 x、y、z 字段转换为浮点后构造的 Vector3 实例。</returns>
    /// <exception cref="ScriptRuntimeException">当 v 不是表类型时抛出，异常消息包含 key 和指示需要 vector3 的说明。</exception>
    public static Vector3 UnpackVector3Args(DynValue v, string key)
    {
        if (v.Type != DataType.Table)
            throw new ScriptRuntimeException($"{key}: 需要 vector3");

        var t = v.Table;
        return new Vector3(
            (float)t.Get("x").Number,
            (float)t.Get("y").Number,
            (float)t.Get("z").Number);
    }
    /// <summary>
    /// 解析 DynValue 表并返回 Quaternion。
    /// </summary>
    /// <param name="v">包含 x、y、z、w 数值字段的 DynValue 表。</param>
    /// <param name="key">用于构造异常消息的键名。</param>
    /// <returns>根据表中的 x、y、z、w 字段构建并返回 Quaternion。</returns>
    /// <exception cref="ScriptRuntimeException">当 v 的类型不是表时抛出，异常消息包含提供的 key。</exception>
    public static Quaternion UnpackQuaternionArgs(DynValue v, string key)
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
    /// <summary>
    /// 创建一个包含 x 和 y 键的 Table，用于表示给定的 Vector2。
    /// </summary>
    /// <param name="script">用于创建返回 Table 的 Script 实例。</param>
    /// <param name="v">要表示的 Vector2 值。</param>
    /// <returns>表示 v 的 Table，包含键 'x' 和 'y'。</returns>
    public static Table PackVector2(Script script, Vector2 v)
    {
        return new(script)
        {
            ["x"] = v.x,
            ["y"] = v.y,
        };
    }
    /// <summary>
    /// 从指定回调参数索引处的表中解包并返回一个包含 x 和 y 分量的 Vector2。
    /// </summary>
    /// <remarks>期望目标表包含可转换为数值的字段 'x' 和 'y'；将其转换为 float 后作为 Vector2 的分量。</remarks>
    /// <param name="args">包含调用时传入参数的集合，用于从中读取表值。</param>
    /// <param name="index">要读取的参数索引（从零开始）。</param>
    /// <param name="sig">用于构建错误消息的函数签名或上下文标识。</param>
    /// <returns>由参数表的 x 和 y 字段构造的 Vector2。</returns>
    /// <exception cref="ScriptRuntimeException">当指定索引处没有参数或该参数不是表时引发，错误消息包含 sig 提供的上下文。</exception>
    public static Vector2 UnpackVector2(CallbackArguments args, int index, string sig)
    {
        if (args.Count <= index || args[index].Type != DataType.Table)
            throw new ScriptRuntimeException($"{sig}: 参数需要 vector2");

        var t = args[index].Table;
        return new Vector2(
            (float)t.Get("x").Number,
            (float)t.Get("y").Number);
    }
    /// <summary>
    /// 将 Vector3 的分量打包为与指定 Script 关联的 Table。
    /// </summary>
    /// <remarks>字段使用向量的数值分量。</remarks>
    /// <param name="script">用于创建并关联返回 Table 的 Script 实例。</param>
    /// <param name="v">要打包的三维向量，其 x、y、z 分量将作为 Table 字段存储。</param>
    /// <returns>包含 x、y、z 字段并关联到提供的 Script 的 Table。</returns>
    public static Table PackVector3(Script script, Vector3 v)
    {
        return new(script)
        {
            ["x"] = v.x,
            ["y"] = v.y,
            ["z"] = v.z,
        };
    }
    /// <summary>
    /// 从 CallbackArguments 的指定索引处解包一个包含 x、y、z 字段的表并返回对应的 Vector3。
    /// </summary>
    /// <param name="args">调用方传入的参数集合，包含要解包的表。</param>
    /// <param name="index">参数集合中要解包的项的索引。</param>
    /// <param name="sig">用于构建错误消息前缀的签名字符串。</param>
    /// <returns>由表中的 x、y、z 字段转换得到的 Vector3（float）。</returns>
    /// <exception cref="ScriptRuntimeException">当索引越界或指定参数不是表时抛出；异常消息以 sig 开头并包含“参数需要 vector3”。</exception>
    public static Vector3 UnpackVector3(CallbackArguments args, int index, string sig)
    {
        if (args.Count <= index || args[index].Type != DataType.Table)
            throw new ScriptRuntimeException($"{sig}: 参数需要 vector3");

        var t = args[index].Table;
        return new Vector3(
            (float)t.Get("x").Number,
            (float)t.Get("y").Number,
            (float)t.Get("z").Number);
    }
    /// <summary>
    /// 将四元数封装为包含 x、y、z 和 w 分量的 Table。
    /// </summary>
    /// <param name="script">用于创建返回 Table 的脚本上下文。</param>
    /// <param name="q">要封装的四元数。</param>
    /// <returns>包含字段 "x"、"y"、"z" 和 "w" 的 Table。</returns>
    public static Table PackQuaternion(Script script, Quaternion q)
    {
        return new(script)
        {
            ["x"] = q.x,
            ["y"] = q.y,
            ["z"] = q.z,
            ["w"] = q.w,
        };
    }
    /// <summary>
    /// 从指定索引处的表中解包为 Quaternion。
    /// </summary>
    /// <remarks>表必须包含可作为数字读取的 x、y、z、w 字段；这些值将转换为 float。</remarks>
    /// <param name="args">包含待解包值的 CallbackArguments 集合。</param>
    /// <param name="index">要从 args 中读取的参数索引。</param>
    /// <param name="sig">用于构建异常消息的签名或上下文字符串。</param>
    /// <returns>由表字段 x、y、z 和 w 构造的 Quaternion。</returns>
    /// <exception cref="ScriptRuntimeException">当索引超出范围或指定参数不是表时抛出；异常消息以 sig 开头并包含“参数需要 quaternion”。</exception>
    public static Quaternion UnpackQuaternion(CallbackArguments args, int index, string sig)
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
    /// <summary>
    /// 将 Bounds 转换为包含 center、size、extents、min 和 max 字段的 Lua 表。
    /// </summary>
    /// <remarks>所有 Vector3 字段通过 PackVector3 转换为 Lua 表并封装为 DynValue。</remarks>
    /// <param name="script">用于创建 DynValue 和 Table 的脚本上下文。</param>
    /// <param name="b">要打包为 Lua 表的 Bounds 值。</param>
    /// <returns>包含 center、size、extents、min 和 max 键的 Table，每个键的值为由 PackVector3 返回的 Lua 表并通过 DynValue.NewTable 封装。</returns>
    public static Table PackBounds(Script script, Bounds b)
    {
        return new(script)
        {
            ["center"] = DynValue.NewTable(PackVector3(script, b.center)),
            ["size"] = DynValue.NewTable(PackVector3(script, b.size)),
            ["extents"] = DynValue.NewTable(PackVector3(script, b.extents)),
            ["min"] = DynValue.NewTable(PackVector3(script, b.min)),
            ["max"] = DynValue.NewTable(PackVector3(script, b.max)),
        };
    }
    /// <summary>
    /// 从 Lua 表中解析并返回 Bounds，要求包含 center 和 size 字段。
    /// </summary>
    /// <remarks>center 和 size 必须为可由 UnpackVector3Args 解析为 Vector3 的表。</remarks>
    /// <param name="v">表示包含 bounds 的 DynValue，必须为 Table。</param>
    /// <param name="key">用于构造错误消息的键名。</param>
    /// <returns>表示解析后的 Bounds，基于解析得到的 center 和 size。</returns>
    /// <exception cref="ScriptRuntimeException">当 v 不是 Table，或 center 或 size 缺失或不是 Table 时抛出，异常消息包含 key。</exception>
    public static Bounds UnpackBoundsArgs(DynValue v, string key)
    {
        if (v.Type != DataType.Table)
            throw new ScriptRuntimeException($"{key}: 需要 bounds");

        var t = v.Table;
        var center = t.Get("center");
        var size = t.Get("size");

        return center.Type != DataType.Table || size.Type != DataType.Table
            ? throw new ScriptRuntimeException($"{key}: bounds 需要 center 和 size")
            : new Bounds(
            UnpackVector3Args(center, key),
            UnpackVector3Args(size, key));
    }
    /// <summary>
    /// 将颜色分量打包为包含 r、g、b、a 字段的 Table，并将其与指定的 Script 关联。
    /// </summary>
    /// <param name="script">用于创建并关联 Table 的 Script 上下文。</param>
    /// <param name="c">要打包为 r、g、b、a 字段的颜色。</param>
    /// <returns>包含 r、g、b、a 四个字段且与指定 Script 关联的 Table。</returns>
    public static Table PackColor(Script script, Color c)
    {
        return new(script)
        {
            ["r"] = c.r,
            ["g"] = c.g,
            ["b"] = c.b,
            ["a"] = c.a,
        };
    }
    /// <summary>
    /// 从 DynValue 表解析 r、g、b 和可选 a 字段并返回对应的 Color。
    /// </summary>
    /// <remarks>字段 r、g、b、a 应为数值；若 a 缺失或为 nil，则使用默认 alpha 值 1.0f。</remarks>
    /// <param name="v">包含 r、g、b 和可选 a 字段的 DynValue（表）。</param>
    /// <param name="key">用于构造错误消息的键名（当 v 不是表时包含在异常消息中）。</param>
    /// <returns>由表中 r、g、b、a 值构成的 Color；当 a 为 nil 时使用 1.0f 作为默认 alpha 值。</returns>
    /// <exception cref="ScriptRuntimeException">当 v 不是表时抛出，异常消息包含 key 并指示需要 color。</exception>
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
