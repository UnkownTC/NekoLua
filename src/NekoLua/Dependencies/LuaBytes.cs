using MoonSharp.Interpreter;

namespace NekoLua.Dependencies;

/// <summary>
/// Lua 表 → 字节数组的**严格**转换。
///
/// 审查 P3「sendBytes 元素静默置 0」：TCP / UDP 的 `sendBytes`（以及 `buffer.writeBytes`）
/// 原先逐元素判断 `v.Type == DataType.Number`，非数字元素被**静默跳过或填 0**，
/// 且越界值按 `(byte)` 静默回绕。脚本把表下标写错、或混入 nil/字符串时，
/// 会把一段完全错误且长度不符的字节流发到网络上，既不报错也无迹可循。
///
/// 现在统一改为：元素非 number → 抛脚本异常；数值越界 [0,255] → 抛脚本异常。
/// </summary>
internal static class LuaBytes
{
    /// <summary>1-based 逐元素转换成字节数组，任一元素不合法即抛，绝不静默补 0。</summary>
    internal static byte[] FromTable(Table arr, string api, int maxBytes = 0)
    {
        if (maxBytes > 0 && arr.Length > maxBytes)
            throw new ScriptRuntimeException($"{api}: 元素数 {arr.Length} 超过上限 {maxBytes}");

        var data = new byte[arr.Length];
        for (var i = 1; i <= arr.Length; i++)
        {
            var v = arr.Get(i);
            if (v.Type != DataType.Number)
                throw new ScriptRuntimeException(
                    $"{api}: 第 {i} 个元素应为 number，实际是 {v.Type}");

            data[i - 1] = LuaNumbers.ToByte(v.Number, $"{api} 第 {i} 个元素");
        }

        return data;
    }
}
