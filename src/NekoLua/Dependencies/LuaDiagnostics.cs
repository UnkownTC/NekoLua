using System.Collections.Concurrent;

using LabApi.Features.Console;

namespace NekoLua.Dependencies;

/// <summary>
/// 「被吞异常」的可观测化工具。
///
/// 审查 P3「空 catch 语义化」：项目内原有 38 处 `catch` 配空块（或只留一句注释），
/// 按 <c>docs/dotnet-coding-standard.md</c> §7.1「**禁止** <c>catch (Exception)</c> 配空块；
/// 必须记录或向上抛」需要逐处处理。
///
/// 这些点绝大多数属于「**容错语义**」——例如遍历 <c>Item.List</c> 时跳过某个已被销毁
/// 的实例、释放连接时忽略底层 socket 已断开——直接改成上抛会把「局部容错」变成
/// 「整批失败」，是不可取的。因此统一改为**记录**，并按调用场景分两档：
///
///   - <see cref="Warn"/> / <see cref="Error"/>：**每次触发都记**。用于清理、释放、
///     用户回调失败等低频且后果明确的路径。
///   - <see cref="WarnOnce"/>：**同一 key 只记一次**。用于「逐元素 / 逐成员」的反射与
///     序列化热路径——那里每次调用可能遍历上百个元素，逐条记日志会直接刷屏，
///     反而淹没真正需要人看的错误。按 key 去重既保留了可观测性，又不会制造噪音。
/// </summary>
internal static class LuaDiagnostics
{
    /// <summary>去重键上限。防止脚本对海量动态类型做反射时字典无界增长。</summary>
    private const int MaxKeys = 1024;

    private static readonly ConcurrentDictionary<string, byte> _seen = [];

    /// <summary>记录一次可自愈的吞异常（每次触发都记）。</summary>
    internal static void Warn(string context, Exception ex)
        => Logger.Warn($"[NekoLua] {context}: {ex.GetType().Name} - {ex.Message}");

    /// <summary>记录一次吞掉的用户回调 / 不可自愈异常（每次触发都记）。</summary>
    internal static void Error(string context, Exception ex)
        => Logger.Error($"[NekoLua] {context}: {ex.GetType().Name} - {ex.Message}");

    /// <summary>
    /// 记录一次吞异常，但**同一 (context, 异常类型) 只记一次**。
    /// 用于逐元素循环等可能高频触发的场合。
    /// </summary>
    internal static void WarnOnce(string context, Exception ex)
    {
        var key = context + "|" + ex.GetType().Name;

        // 已达键上限：放弃记录（底层操作本身仍是容错的，只是不再产生新诊断行）
        if (_seen.Count >= MaxKeys)
            return;

        if (!_seen.TryAdd(key, 0))
            return;

        Warn(context, ex);
    }
}
