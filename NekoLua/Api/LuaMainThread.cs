using LabApi.Features.Console;
using System.Collections.Concurrent;

namespace NekoLua.Api;

/// <summary>
/// 为主线程调度和执行 Action 回调的线程安全静态队列，使用配置的队列大小和每次 Tick 处理数量限制。
/// </summary>
/// <remarks>使用 ConcurrentQueue<Action> 和 Interlocked 管理计数。Queue 在超过 MainThreadQueueLimit 时返回 false 并拒绝入队。Tick
/// 每次最多处理 MainThreadTasksPerTick 个任务，执行时捕获并记录异常。Clear 在插件禁用时清空队列以移除对已卸载脚本或连接的残留引用。</remarks>
public static class LuaMainThread
{
    private static readonly ConcurrentQueue<Action> _queue = new();
    private static int _count;
    private static int MaxQueueSize => MainPlugin.CacheConfig.MainThreadQueueLimit;
    private static int MaxPerTick => MainPlugin.CacheConfig.MainThreadTasksPerTick;
    /// <summary>
    /// 将指定的操作添加到内部队列以便稍后执行；当队列达到最大容量时不添加并返回 false。
    /// </summary>
    /// <remarks>方法使用原子计数检测并限制队列大小，保证并发环境下的一致性。入队成功后将操作放入内部队列；入队失败时会回滚计数且不会修改队列。调用方负责在适当时机执行队列中的操作。</remarks>
    /// <param name="action">要排队以便稍后执行的操作委托。</param>
    /// <returns>若操作成功加入队列则返回 true；若队列已满则返回 false。</returns>
    public static bool Queue(Action action)
    {
        if (Interlocked.Increment(ref _count) > MaxQueueSize)
        {
            _ = Interlocked.Decrement(ref _count);
            return false;
        }

        _queue.Enqueue(action);
        return true;
    }
    internal static void Tick()
    {
        var processed = 0;
        var limit = MaxPerTick;

        while (processed < limit && _queue.TryDequeue(out var action))
        {
            _ = Interlocked.Decrement(ref _count);
            processed++;

            try
            { action(); }
            catch (Exception ex)
            {
                Logger.Error($"主线程任务异常: {ex.Message}");
            }
        }
    }
    internal static void Clear()
    {
        while (_queue.TryDequeue(out _))
            _ = Interlocked.Decrement(ref _count);
    }
}
