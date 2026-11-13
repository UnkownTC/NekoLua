using MoonSharp.Interpreter;

namespace NekoLua.Dependencies;

/// <summary>
/// Lua 数字 → CLR 整数/无符号数的**带范围校验**转换。
///
/// 审查 P3「无符号强转范围校验」：原实现直接写 `(byte)d` / `(ushort)d` / `(uint)d`，
/// 在 C# 非受检上下文里越界值会**静默回绕**（如 `(byte)300.0 == 44`、`(ushort)-1 == 65535`、
/// `(byte)double.NaN == 0`）。脚本传错参数时既拿不到异常、也看不出结果为何不对，
/// 会给下游（字节流 / 序列号 / 端口 / ControllerId）注入完全错误的数值。
///
/// 这里统一在转换前做范围判定：**范围内保持原有行为**（小数按截断处理，与原实现一致），
/// **越界则抛 Lua 脚本异常**，让脚本能立刻发现参数错误。
/// </summary>
internal static class LuaNumbers
{
    /// <summary>范围校验并截断取整。越界 / NaN / Inf 抛 <see cref="ScriptRuntimeException"/>。</summary>
    private static long Checked(double d, long min, long max, string what)
    {
        if (double.IsNaN(d) || double.IsInfinity(d))
            throw new ScriptRuntimeException($"{what}: 数值 {d} 不是有效整数");

        var t = Math.Truncate(d);
        if (t < min || t > max)
            throw new ScriptRuntimeException($"{what}: 数值 {d} 超出范围 [{min}, {max}]");

        return (long)t;
    }

    /// <summary>
    /// 范围校验 + **饱和截断**：越界值夹到 [0, 255]，NaN / Inf 抛异常。
    /// 用于语义上「本意就是夹取」的场合（如 AmnesticCloud 的 size），
    /// 保留原有夹取行为的同时消除 `(byte)double.NaN == 0` 这一静默通道。
    /// </summary>
    internal static byte ClampToByte(double d, string what)
    {
        if (double.IsNaN(d) || double.IsInfinity(d))
            throw new ScriptRuntimeException($"{what}: 数值 {d} 不是有效数字");

        var t = Math.Truncate(d);
        if (t < byte.MinValue) return byte.MinValue;
        if (t > byte.MaxValue) return byte.MaxValue;
        return (byte)t;
    }

    /// <summary>
    /// Unix 秒（可含小数）→ 毫秒时间戳。
    /// `(long)(seconds * 1000)` 在越界 / NaN 时会被 CLR 饱和成 <see cref="long.MinValue"/> /
    /// <see cref="long.MaxValue"/>，随后交给 <c>DateTimeOffset.FromUnixTimeMilliseconds</c>
    /// 会抛出裸的 <c>ArgumentOutOfRangeException</c>。这里提前判定，
    /// 换成脚本可读的异常信息。
    /// </summary>
    internal static long ToUnixMilliseconds(double seconds, string what)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds))
            throw new ScriptRuntimeException($"{what}: 时间戳 {seconds} 不是有效数值");

        var ms = seconds * 1000.0;
        if (ms < DateTimeOffset.MinValue.ToUnixTimeMilliseconds() ||
            ms > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
            throw new ScriptRuntimeException($"{what}: 时间戳 {seconds} 超出可表示范围");

        return (long)ms;
    }

    internal static byte ToByte(double d, string what) => (byte)Checked(d, byte.MinValue, byte.MaxValue, what);

    internal static sbyte ToSByte(double d, string what) => (sbyte)Checked(d, sbyte.MinValue, sbyte.MaxValue, what);

    internal static short ToInt16(double d, string what) => (short)Checked(d, short.MinValue, short.MaxValue, what);

    internal static ushort ToUInt16(double d, string what) => (ushort)Checked(d, ushort.MinValue, ushort.MaxValue, what);

    internal static int ToInt32(double d, string what) => (int)Checked(d, int.MinValue, int.MaxValue, what);

    internal static uint ToUInt32(double d, string what) => (uint)Checked(d, uint.MinValue, uint.MaxValue, what);

    internal static long ToInt64(double d, string what) => Checked(d, long.MinValue, long.MaxValue, what);

    internal static ulong ToUInt64(double d, string what)
    {
        // ulong 上界超出 long 可表示范围，单独判定，避免 (long) 溢出
        if (double.IsNaN(d) || double.IsInfinity(d))
            throw new ScriptRuntimeException($"{what}: 数值 {d} 不是有效整数");

        var t = Math.Truncate(d);
        if (t < 0 || t > ulong.MaxValue)
            throw new ScriptRuntimeException($"{what}: 数值 {d} 超出范围 [0, {ulong.MaxValue}]");

        return (ulong)t;
    }

    /// <summary>按 <see cref="TypeCode"/> 分派；不支持的类型返回 <c>null</c> 由调用方决定如何报错。</summary>
    internal static object? TryConvert(TypeCode code, double d, string what) => code switch
    {
        TypeCode.Byte => ToByte(d, what),
        TypeCode.SByte => ToSByte(d, what),
        TypeCode.Int16 => ToInt16(d, what),
        TypeCode.UInt16 => ToUInt16(d, what),
        TypeCode.Int32 => ToInt32(d, what),
        TypeCode.UInt32 => ToUInt32(d, what),
        TypeCode.Int64 => ToInt64(d, what),
        TypeCode.UInt64 => ToUInt64(d, what),
        _ => null,
    };
}
