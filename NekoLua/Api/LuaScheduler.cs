using LabApi.Features.Console;

using MoonSharp.Interpreter;
using NekoLua.Loader;

namespace NekoLua.Api;

internal static class LuaScheduler
{
    private enum Kind { Daily, Weekly, At }
    private sealed class Task
    {
        public required string Id;
        public required Kind Kind;
        public required DynValue Callback;
        public required LuaModInstance Owner;
        public DateTime NextTrigger;
        public TimeSpan TimeOfDay;
        public DayOfWeek Weekday;
        public bool Cancelled;
    }
    private static readonly List<Task> _tasks = [];
    private static DateTime _lastCheck = DateTime.MinValue;
    public static void Daily(TimeSpan time, string id, DynValue cb, LuaModInstance owner)
    {
        var now = DateTime.Now;
        _tasks.Add(new Task
        {
            Id = id,
            Kind = Kind.Daily,
            TimeOfDay = time,
            NextTrigger = NextDailyTime(now, time),
            Callback = cb,
            Owner = owner,
        });
    }
    public static void Weekly(DayOfWeek day, TimeSpan time, string id, DynValue cb, LuaModInstance owner)
    {
        var now = DateTime.Now;
        _tasks.Add(new Task
        {
            Id = id,
            Kind = Kind.Weekly,
            TimeOfDay = time,
            Weekday = day,
            NextTrigger = NextWeeklyTime(now, day, time),
            Callback = cb,
            Owner = owner,
        });
    }
    public static void At(DateTime target, string id, DynValue cb, LuaModInstance owner)
    {
        _tasks.Add(new Task
        {
            Id = id,
            Kind = Kind.At,
            NextTrigger = target,
            Callback = cb,
            Owner = owner,
        });
    }
    public static int Cancel(string id, LuaModInstance owner)
    {
        var n = 0;
        foreach (var t in _tasks)
            if (!t.Cancelled && ReferenceEquals(t.Owner, owner) && t.Id == id)
            { t.Cancelled = true; n++; }
        return n;
    }
    public static int CancelAll(LuaModInstance owner)
    {
        var n = 0;
        foreach (var t in _tasks)
            if (!t.Cancelled && ReferenceEquals(t.Owner, owner))
            { t.Cancelled = true; n++; }
        return n;
    }
    public static int Count(LuaModInstance owner)
    {
        var n = 0;
        foreach (var t in _tasks)
            if (!t.Cancelled && ReferenceEquals(t.Owner, owner))
                n++;
        return n;
    }
    public static void CleanupFor(LuaModInstance mod)
    {
        foreach (var t in _tasks)
            if (ReferenceEquals(t.Owner, mod))
                t.Cancelled = true;

        _tasks.RemoveAll(t => t.Cancelled);
    }
    public static void ClearAll()
    {
        _tasks.Clear();
        _lastCheck = DateTime.MinValue;
    }
    public static void Tick()
    {
        var now = DateTime.Now;

        if ((now - _lastCheck).TotalSeconds < 1)
            return;
        _lastCheck = now;

        for (var i = _tasks.Count - 1; i >= 0; i--)
        {
            var t = _tasks[i];

            if (t.Cancelled)
            {
                _tasks.RemoveAt(i);
                continue;
            }

            if (now < t.NextTrigger)
                continue;

            try
            {
                _ = t.Callback.Function.Call();
            }
            catch (InterpreterException ex)
            {
                Logger.Error($"[{t.Owner.Name}] scheduler 回调异常: {ex.DecoratedMessage}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[{t.Owner.Name}] scheduler 回调异常: {ex.Message}");
            }

            if (t.Kind == Kind.At)
            {
                _tasks.RemoveAt(i);
                continue;
            }

            t.NextTrigger = t.Kind switch
            {
                Kind.Daily => NextDailyTime(now, t.TimeOfDay),
                Kind.Weekly => NextWeeklyTime(now, t.Weekday, t.TimeOfDay),
                _ => now,
            };
        }
    }
    private static DateTime NextDailyTime(DateTime now, TimeSpan time)
    {
        var today = now.Date + time;
        return today > now ? today : today.AddDays(1);
    }
    private static DateTime NextWeeklyTime(DateTime now, DayOfWeek day, TimeSpan time)
    {
        var today = now.Date + time;
        var daysAhead = ((int)day - (int)now.DayOfWeek + 7) % 7;
        var candidate = today.AddDays(daysAhead);

        if (candidate <= now)
            candidate = candidate.AddDays(7);

        return candidate;
    }
}
