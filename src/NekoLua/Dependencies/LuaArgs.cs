using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;

namespace NekoLua.Dependencies;

/// <summary>
/// Lua 实参解析小工具：把「玩家」这类在 Lua 侧有多种写法（代理表 / userId 字符串 /
/// networkId 数字）的入参统一收敛成一个解析口径，避免每个代理各写一份。
/// </summary>
public static class LuaArgs
{
    /// <summary>
    /// 解析玩家标识（字符串或 player 代理表）为 userId。
    /// </summary>
    public static string ResolveUserId(CallbackArguments args, int index, string sig)
    {
        if (args.Count <= index)
            throw new ScriptRuntimeException(sig);

        var a = args[index];

        if (a.Type == DataType.String)
            return a.String;

        if (a.Type == DataType.Table)
        {
            var userId = a.Table.Get("userId");

            if (userId.Type == DataType.String)
                return userId.String;

            if (LuaObjectProxy.Unwrap(a.Table) is Player fromTable)
                return fromTable.UserId;
        }

        throw new ScriptRuntimeException($"{sig}（需要 userId 字符串或 player）");
    }

    /// <summary>
    /// 解析实参为 <see cref="Player"/>：接受 player 代理表、userId/昵称字符串、networkId/playerId 数字。
    /// 找不到时返回 <c>null</c>（调用方决定是抛错还是返回 nil）。
    /// </summary>
    public static Player? TryPlayer(CallbackArguments args, int index, string sig)
    {
        if (args.Count <= index)
            throw new ScriptRuntimeException(sig);

        var a = args[index];

        switch (a.Type)
        {
            case DataType.Table:
                if (LuaObjectProxy.Unwrap(a.Table) is Player fromTable)
                    return fromTable;
                break;

            case DataType.String:
                return Player.Get(a.String);

            case DataType.Number:
                {
                    var n = a.Number;

                    // 审查 P3：原写法 `(int)n` 在 n 超出 int 范围时是 CLR 未定义结果，
                    // 仅靠随后的 `(uint)n` 兜底。这里显式限定可表示区间后再分类查找。
                    if (n != Math.Floor(n) ||
                        double.IsNaN(n) ||
                        n < 0 ||
                        n > uint.MaxValue)
                        return null;

                    return n <= int.MaxValue
                        ? Player.Get((int)n) ?? Player.Get((uint)n)
                        : Player.Get((uint)n);
                }
        }

        throw new ScriptRuntimeException($"{sig}（需要 player / userId / networkId）");
    }

    /// <summary>
    /// 解析实参为 <see cref="Player"/>，找不到时抛 <see cref="ScriptRuntimeException"/>。
    /// </summary>
    public static Player ResolvePlayer(CallbackArguments args, int index, string sig)
        => TryPlayer(args, index, sig)
           ?? throw new ScriptRuntimeException($"{sig}: 找不到该玩家（需在线）");

    /// <summary>
    /// 自 <paramref name="startIndex"/> 起收集剩余实参为字符串数组（至少要求 <paramref name="min"/> 个）。
    /// </summary>
    public static string[] CollectStrings(
        CallbackArguments args,
        int startIndex,
        int min,
        string sig)
    {
        var list = new List<string>(Math.Max(args.Count - startIndex, 0));

        for (var i = startIndex; i < args.Count; i++)
        {
            if (args[i].Type != DataType.String)
                throw new ScriptRuntimeException($"{sig}: 第 {i - startIndex + 1} 个参数需要字符串");

            list.Add(args[i].String);
        }

        if (list.Count < min)
            throw new ScriptRuntimeException(sig);

        return [.. list];
    }

    /// <summary>
    /// 把字符串序列打包为 Lua 数组表。
    /// </summary>
    public static Table StringArray(Script script, IEnumerable<string> items)
    {
        var arr = new Table(script);
        var i = 1;

        foreach (var s in items)
            arr[i++] = DynValue.NewString(s);

        return arr;
    }

    /// <summary>
    /// 解析 Flags 枚举实参：字符串按名称（支持 <c>"A, B"</c> 逗号组合），数字按位值。
    /// </summary>
    public static T ParseFlags<T>(DynValue v, string key) where T : struct, Enum
    {
        if (v.Type == DataType.Number)
            return (T)Enum.ToObject(typeof(T), (long)v.Number);

        return ParseEnum<T>(v, key);
    }

    /// <summary>
    /// 把字符串实参解析为枚举 <typeparamref name="T"/>（忽略大小写），
    /// 失败时抛出含全部合法取值的错误，便于脚本自纠。
    /// </summary>
    public static T ParseEnum<T>(DynValue v, string key) where T : struct, Enum
    {
        if (v.Type != DataType.String)
            throw new ScriptRuntimeException($"{key}: 需要字符串");

        if (Enum.TryParse<T>(v.String, true, out var parsed))
            return parsed;

        throw new ScriptRuntimeException(
            $"{key}: 未知取值 '{v.String}'，可选: {string.Join(" / ", Enum.GetNames(typeof(T)))}");
    }
}
