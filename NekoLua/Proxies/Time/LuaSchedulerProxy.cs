using MoonSharp.Interpreter;
using NekoLua.Api;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Time;

/// <summary>
/// scheduler 表：
///   scheduler.daily("08:00", [id], callback)
///   scheduler.weekly("monday", "09:00", [id], callback)
///   scheduler.at("2026-10-01 12:00:00", [id], callback)
///   scheduler.cancel(id) / cancelAll() / count()
/// </summary>
internal class LuaSchedulerProxy : IRegisterLua
{
    public string Name { get; } = "scheduler";
    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);

        t["daily"] = DynValue.NewCallback((_, args) =>
        {
            const string sig = "scheduler.daily(time, [id], callback)";

            var time = ParseTime(args, 0, sig);
            var (id, cb) = ParseIdAndCallback(args, 1, sig);

            LuaScheduler.Daily(time, id, cb, mod);
            return DynValue.Nil;
        });

        t["weekly"] = DynValue.NewCallback((_, args) =>
        {
            const string sig = "scheduler.weekly(weekday, time, [id], callback)";

            if (args.Count < 3 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException(sig);

            var day = ParseWeekday(args[0].String, sig);
            var time = ParseTime(args, 1, sig);
            var (id, cb) = ParseIdAndCallback(args, 2, sig);

            LuaScheduler.Weekly(day, time, id, cb, mod);
            return DynValue.Nil;
        });

        t["at"] = DynValue.NewCallback((_, args) =>
        {
            const string sig = "scheduler.at(datetime, [id], callback)";

            if (args.Count < 2 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException(sig);

            if (!DateTime.TryParse(args[0].String, out var target))
                throw new ScriptRuntimeException(
                    $"{sig}: 无法解析日期 '{args[0].String}'，建议格式 'yyyy-MM-dd HH:mm:ss'");

            var (id, cb) = ParseIdAndCallback(args, 1, sig);

            LuaScheduler.At(target, id, cb, mod);
            return DynValue.Nil;
        });

        t["cancel"] = DynValue.NewCallback((_, args) =>
        {
            return args.Count < 1 || args[0].Type != DataType.String
                ? throw new ScriptRuntimeException("scheduler.cancel(id)")
                : DynValue.NewNumber(LuaScheduler.Cancel(args[0].String, mod));
        });

        t["cancelAll"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LuaScheduler.CancelAll(mod)));

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LuaScheduler.Count(mod)));

        return t;
    }
    private static (string? id, DynValue cb) ParseIdAndCallback(
        CallbackArguments args, int start, string sig)
    {
        if (start >= args.Count)
            throw new ScriptRuntimeException(sig);

        var a = args[start];

        if (a.Type == DataType.Function)
            return (null, a);

        if (a.Type == DataType.String && start + 1 < args.Count
            && args[start + 1].Type == DataType.Function)
        {
            var id = a.String;
            return string.IsNullOrWhiteSpace(id) ? throw new ScriptRuntimeException($"{sig}: id 不能为空") : ((string? id, DynValue cb))(id, args[start + 1]);
        }

        throw new ScriptRuntimeException(sig);
    }
    private static TimeSpan ParseTime(CallbackArguments args, int index, string sig)
    {
        if (args.Count <= index || args[index].Type != DataType.String)
            throw new ScriptRuntimeException(sig);

        var s = args[index].String;
        var parts = s.Split(':');

        if (parts.Length is < 2 or > 3)
            throw new ScriptRuntimeException($"{sig}: 时间格式 'HH:mm' 或 'HH:mm:ss'，收到 '{s}'");

        if (!int.TryParse(parts[0], out var h) || h < 0 || h > 23)
            throw new ScriptRuntimeException($"{sig}: 小时非法 '{parts[0]}'");

        if (!int.TryParse(parts[1], out var m) || m < 0 || m > 59)
            throw new ScriptRuntimeException($"{sig}: 分钟非法 '{parts[1]}'");

        var sec = 0;
        return parts.Length == 3 && (!int.TryParse(parts[2], out sec) || sec < 0 || sec > 59)
            ? throw new ScriptRuntimeException($"{sig}: 秒非法 '{parts[2]}'")
            : new TimeSpan(h, m, sec);
    }
    private static DayOfWeek ParseWeekday(string s, string sig)
    {
        return s.ToLowerInvariant() switch
        {
            "sun" or "sunday" => DayOfWeek.Sunday,
            "mon" or "monday" => DayOfWeek.Monday,
            "tue" or "tuesday" => DayOfWeek.Tuesday,
            "wed" or "wednesday" => DayOfWeek.Wednesday,
            "thu" or "thursday" => DayOfWeek.Thursday,
            "fri" or "friday" => DayOfWeek.Friday,
            "sat" or "saturday" => DayOfWeek.Saturday,
            _ => throw new ScriptRuntimeException(
                    $"{sig}: 未知 weekday '{s}'，可选 sun/mon/tue/wed/thu/fri/sat"),
        };
    }
}
