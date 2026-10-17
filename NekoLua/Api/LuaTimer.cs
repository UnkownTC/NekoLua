using LabApi.Features.Console;

using MoonSharp.Interpreter;
using NekoLua.Loader;

namespace NekoLua.Api;

internal static class LuaTimer
{
    private static int MaxConsecutiveErrors
    {
        get
        {
            var v = MainPlugin.CacheConfig.TimerMaxConsecutiveErrors;
            return v <= 0 ? int.MaxValue : v;
        }
    }
    private sealed class Entry
    {
        public object? Id;
        public double Remaining;
        public double Interval;
        public DynValue Callback;
        public LuaModInstance Owner;
        public bool Cancelled;
        public int ErrorStreak;
    }

    private static readonly List<Entry> _active = [];
    private static readonly List<Entry> _pending = [];

    public static void Delay(double seconds, object? id, DynValue callback, LuaModInstance owner)
    {
        _pending.Add(new Entry
        {
            Id = id,
            Remaining = Math.Max(0, seconds),
            Interval = 0,
            Callback = callback,
            Owner = owner,
        });
    }

    public static void Every(double seconds, object? id, DynValue callback, LuaModInstance owner)
    {
        if (seconds <= 0)
            seconds = 0.001;

        _pending.Add(new Entry
        {
            Id = id,
            Remaining = 0,
            Interval = seconds,
            Callback = callback,
            Owner = owner,
        });
    }

    public static void EveryLate(double seconds, object? id, DynValue callback, LuaModInstance owner)
    {
        if (seconds <= 0)
            seconds = 0.001;

        _pending.Add(new Entry
        {
            Id = id,
            Remaining = seconds,
            Interval = seconds,
            Callback = callback,
            Owner = owner,
        });
    }
    public static int Cancel(object id, LuaModInstance owner)
    {
        var n = 0;

        foreach (var e in _active)
            if (!e.Cancelled && ReferenceEquals(e.Owner, owner) && Equals(e.Id, id))
            { e.Cancelled = true; n++; }

        foreach (var e in _pending)
            if (!e.Cancelled && ReferenceEquals(e.Owner, owner) && Equals(e.Id, id))
            { e.Cancelled = true; n++; }

        return n;
    }

    public static int CancelAll(LuaModInstance owner)
    {
        var n = 0;

        foreach (var e in _active)
            if (!e.Cancelled && ReferenceEquals(e.Owner, owner))
            { e.Cancelled = true; n++; }

        foreach (var e in _pending)
            if (!e.Cancelled && ReferenceEquals(e.Owner, owner))
            { e.Cancelled = true; n++; }

        return n;
    }

    public static int Count(LuaModInstance owner)
    {
        var n = 0;

        foreach (var e in _active)
            if (!e.Cancelled && ReferenceEquals(e.Owner, owner))
                n++;

        foreach (var e in _pending)
            if (!e.Cancelled && ReferenceEquals(e.Owner, owner))
                n++;

        return n;
    }

    public static void ClearAll()
    {
        _active.Clear();
        _pending.Clear();
    }
    public static void Tick(double deltaTime)
    {
        if (deltaTime <= 0)
            return;

        if (_pending.Count > 0)
        {
            _active.AddRange(_pending);
            _pending.Clear();
        }

        var limit = MaxConsecutiveErrors;   // 缓存一次，避免每 tick 反复读

        for (var i = _active.Count - 1; i >= 0; i--)
        {
            var e = _active[i];

            if (e.Cancelled)
            {
                _active.RemoveAt(i);
                continue;
            }

            e.Remaining -= deltaTime;
            if (e.Remaining > 0)
                continue;

            try
            {
                _ = e.Callback.Function.Call();
                e.ErrorStreak = 0;
            }
            catch (InterpreterException ex)
            {
                Logger.Error($"[{e.Owner.Name}] timer 回调异常: {ex.DecoratedMessage}");
                e.ErrorStreak++;
            }
            catch (Exception ex)
            {
                Logger.Error($"[{e.Owner.Name}] timer 回调异常: {ex.GetType().Name}");
                Logger.Error($"[{e.Owner.Name}]   {ex.Message}");
                e.ErrorStreak++;
            }

            if (e.Interval <= 0)
            {
                _active.RemoveAt(i);
                continue;
            }

            if (e.ErrorStreak >= limit)
            {
                Logger.Error(
                    $"[{e.Owner.Name}] timer 已连续出错 {e.ErrorStreak} 次，自动移除" +
                    (e.Id is null ? "" : $" (id={e.Id})"));
                _active.RemoveAt(i);
                continue;
            }

            e.Remaining = e.Interval;
        }
    }
}
