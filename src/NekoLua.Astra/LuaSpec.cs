using MoonSharp.Interpreter;

using UnityEngine;

namespace NekoLua.AstraBridge;

/// <summary>
/// Lua 定义表的读取工具：所有错误都转成带字段路径的 <see cref="ScriptRuntimeException"/>，
/// 让脚本作者能在控制台直接看到是哪个字段写错了。
/// </summary>
internal static class LuaSpec
{
    public static ScriptRuntimeException Error(string ctx, string message) => new($"{ctx}: {message}");

    public static string RequiredString(Table table, string key, string ctx)
    {
        var value = table.Get(key);

        if (value.Type != DataType.String || string.IsNullOrWhiteSpace(value.String))
            throw Error(ctx, $"{key} 必须是非空字符串");

        return value.String;
    }

    public static string? OptionalString(Table table, string key, string ctx)
    {
        var value = table.Get(key);

        if (value.IsNil())
            return null;

        if (value.Type != DataType.String)
            throw Error(ctx, $"{key} 必须是字符串");

        return value.String;
    }

    public static float OptionalNumber(Table table, string key, string ctx, float fallback)
    {
        var value = table.Get(key);

        if (value.IsNil())
            return fallback;

        if (value.Type != DataType.Number)
            throw Error(ctx, $"{key} 必须是数字");

        return (float)value.Number;
    }

    public static bool OptionalBool(Table table, string key, string ctx, bool fallback)
    {
        var value = table.Get(key);

        if (value.IsNil())
            return fallback;

        if (value.Type != DataType.Boolean)
            throw Error(ctx, $"{key} 必须是布尔值");

        return value.Boolean;
    }

    public static TEnum OptionalEnum<TEnum>(Table table, string key, string ctx, TEnum fallback)
        where TEnum : struct, Enum
    {
        var value = table.Get(key);

        if (value.IsNil())
            return fallback;

        if (value.Type != DataType.String)
            throw Error(ctx, $"{key} 必须是字符串枚举名");

        if (!Enum.TryParse(value.String, true, out TEnum parsed) || !Enum.IsDefined(typeof(TEnum), parsed))
            throw Error(ctx, $"{key} = '{value.String}' 不是合法取值");

        return parsed;
    }

    public static string[] OptionalStringArray(Table table, string key, string ctx)
    {
        var value = table.Get(key);

        if (value.IsNil())
            return [];

        if (value.Type != DataType.Table)
            throw Error(ctx, $"{key} 必须是数组");

        List<string> result = [];

        foreach (var item in Sequence(value.Table))
        {
            if (item.Type != DataType.String)
                throw Error(ctx, $"{key} 的每一项都必须是字符串");

            result.Add(item.String);
        }

        return [.. result];
    }

    public static Dictionary<string, int>? OptionalIntMap(Table table, string key, string ctx)
    {
        var value = table.Get(key);

        if (value.IsNil())
            return null;

        if (value.Type != DataType.Table)
            throw Error(ctx, $"{key} 必须是 Id → 数量的映射表");

        Dictionary<string, int> result = new(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in value.Table.Pairs)
        {
            if (pair.Key.Type != DataType.String)
                throw Error(ctx, $"{key} 的键必须是字符串");

            if (pair.Value.Type != DataType.Number)
                throw Error(ctx, $"{key}.{pair.Key.String} 必须是数字");

            result[pair.Key.String] = (int)pair.Value.Number;
        }

        return result;
    }

    public static Vector3? OptionalVector3(Table table, string key, string ctx)
    {
        var value = table.Get(key);

        if (value.IsNil())
            return null;

        if (value.Type != DataType.Table)
            throw Error(ctx, $"{key} 必须是 {{x,y,z}}");

        var t = value.Table;

        var x = t.Get("x");
        var y = t.Get("y");
        var z = t.Get("z");

        if (!x.IsNil() || !y.IsNil() || !z.IsNil())
        {
            return new Vector3(
                (float)(x.IsNil() ? 0 : x.Number),
                (float)(y.IsNil() ? 0 : y.Number),
                (float)(z.IsNil() ? 0 : z.Number));
        }

        var seq = Sequence(t).ToArray();
        if (seq.Length < 3)
            throw Error(ctx, $"{key} 必须是 {{x,y,z}}");

        return new Vector3((float)seq[0].Number, (float)seq[1].Number, (float)seq[2].Number);
    }

    public static DynValue? OptionalCallback(Table table, string key, string ctx)
    {
        var value = table.Get(key);

        if (value.IsNil())
            return null;

        if (value.Type != DataType.Function)
            throw Error(ctx, $"{key} 必须是函数");

        return value;
    }

    public static IEnumerable<DynValue> Sequence(Table table)
    {
        for (var i = 1; ; i++)
        {
            var value = table.Get(i);

            if (value.IsNil())
                yield break;

            yield return value;
        }
    }
}
