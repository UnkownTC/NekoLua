using System.Collections.Concurrent;

using LabApi.Features.Console;

namespace NekoLua.Dependencies;

public static class LuaMainThread
{
    private static readonly ConcurrentQueue<Action> _queue = new();
    private static int _count;
    private static int MaxQueueSize => MainPlugin.CacheConfig.MainThreadQueueLimit;
    private static int MaxPerTick => MainPlugin.CacheConfig.MainThreadTasksPerTick;
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
    public static void Tick()
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

    // 插件 Disable 时清空队列：残留回调引用已卸载的 Script/连接，
    // 若留到下次 Enable 会被新组件突兀执行（审查 P3-33/31）
    public static void Clear()
    {
        while (_queue.TryDequeue(out _))
            _ = Interlocked.Decrement(ref _count);
    }
}
