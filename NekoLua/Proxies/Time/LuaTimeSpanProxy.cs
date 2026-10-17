using System.Globalization;

using MoonSharp.Interpreter;
using NekoLua.Api;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Time;

/// <summary>
/// timespan 表（秒为单位的时长工具）：
///   构造：fromMilliseconds / fromSeconds / fromMinutes / fromHours / fromDays
///         of({days=,hours=,minutes=,seconds=,milliseconds=})
///   换算：seconds / milliseconds / minutes / hours / days / parts
///   格式：format(ts[, fmt])
///   运算：add / sub / mul / div / neg / abs
///   判断：zero / isZero / compare / clamp
/// </summary>
internal class LuaTimeSpanProxy : IRegisterLua
{
    public string Name { get; } = "timespan";

    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var t = new Table(script);

        t["fromMilliseconds"] = Const(_ => _ / 1000.0, "timespan.fromMilliseconds(n)");
        t["fromSeconds"] = Const(_ => _, "timespan.fromSeconds(n)");
        t["fromMinutes"] = Const(_ => _ * 60, "timespan.fromMinutes(n)");
        t["fromHours"] = Const(_ => _ * 3600, "timespan.fromHours(n)");
        t["fromDays"] = Const(_ => _ * 86400, "timespan.fromDays(n)");

        t["of"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Table)
                throw new ScriptRuntimeException("timespan.of(t): 需要一个 table");

            var cfg = args[0].Table;

            double days = NumOf(cfg, "days");
            double hours = NumOf(cfg, "hours");
            double minutes = NumOf(cfg, "minutes");
            double seconds = NumOf(cfg, "seconds");
            double ms = NumOf(cfg, "milliseconds");

            return DynValue.NewNumber(
                days * 86400 + hours * 3600 + minutes * 60 + seconds + ms / 1000.0);
        });

        t["seconds"] = Num(ts => ts, "timespan.seconds(ts)");
        t["milliseconds"] = Num(ts => ts * 1000, "timespan.milliseconds(ts)");
        t["minutes"] = Num(ts => ts / 60, "timespan.minutes(ts)");
        t["hours"] = Num(ts => ts / 3600, "timespan.hours(ts)");
        t["days"] = Num(ts => ts / 86400, "timespan.days(ts)");

        t["parts"] = DynValue.NewCallback((_, args) =>
        {
            var ts = ArgNum(args, 0, "timespan.parts(ts)");
            var span = TimeSpan.FromSeconds(ts);

            var result = new Table(script)
            {
                ["days"] = (int)span.TotalDays,
                ["hours"] = span.Hours,
                ["minutes"] = span.Minutes,
                ["seconds"] = span.Seconds,
                ["milliseconds"] = span.Milliseconds,
            };
            return DynValue.NewTable(result);
        });

        t["format"] = DynValue.NewCallback((_, args) =>
        {
            var ts = ArgNum(args, 0, "timespan.format(ts[, fmt])");
            var span = TimeSpan.FromSeconds(ts);

            string text = args.Count > 1 && args[1].Type == DataType.String
                ? span.ToString(args[1].String, CultureInfo.InvariantCulture)
                : Humanize(span);

            return DynValue.NewString(text);
        });

        t["add"] = Bin((a, b) => a + b, "timespan.add(a, b)");
        t["sub"] = Bin((a, b) => a - b, "timespan.sub(a, b)");

        t["mul"] = DynValue.NewCallback((_, args) =>
        {
            var ts = ArgNum(args, 0, "timespan.mul(ts, k)");
            if (args.Count < 2 || args[1].Type != DataType.Number)
                throw new ScriptRuntimeException("timespan.mul(ts, k)");

            return DynValue.NewNumber(ts * args[1].Number);
        });

        t["div"] = DynValue.NewCallback((_, args) =>
        {
            var ts = ArgNum(args, 0, "timespan.div(ts, k)");
            if (args.Count < 2 || args[1].Type != DataType.Number)
                throw new ScriptRuntimeException("timespan.div(ts, k)");

            var k = args[1].Number;
            if (k == 0)
                throw new ScriptRuntimeException("timespan.div: 除数为 0");

            return DynValue.NewNumber(ts / k);
        });

        t["neg"] = Un(ts => -ts, "timespan.neg(ts)");
        t["abs"] = Un(Math.Abs, "timespan.abs(ts)");

        t["zero"] = DynValue.NewNumber(0);
        t["isZero"] = DynValue.NewCallback((_, args) =>
        {
            var ts = ArgNum(args, 0, "timespan.isZero(ts)");
            return DynValue.NewBoolean(ts == 0);
        });

        t["compare"] = DynValue.NewCallback((_, args) =>
        {
            var a = ArgNum(args, 0, "timespan.compare(a, b)");
            var b = ArgNum(args, 1, "timespan.compare(a, b)");
            return DynValue.NewNumber(Math.Sign(a.CompareTo(b)));
        });

        t["clamp"] = DynValue.NewCallback((_, args) =>
        {
            var ts = ArgNum(args, 0, "timespan.clamp(ts, min, max)");
            var min = ArgNum(args, 1, "timespan.clamp(ts, min, max)");
            var max = ArgNum(args, 2, "timespan.clamp(ts, min, max)");

            return DynValue.NewNumber(LuaExtendMath.Clamp(ts, min, max));
        });

        return t;
    }

    static DynValue Const(Func<double, double> f, string sig)
        => DynValue.NewCallback((_, args) =>
            DynValue.NewNumber(f(ArgNum(args, 0, sig))));

    static DynValue Num(Func<double, double> f, string sig)
        => DynValue.NewCallback((_, args) =>
            DynValue.NewNumber(f(ArgNum(args, 0, sig))));

    static DynValue Un(Func<double, double> f, string sig)
        => DynValue.NewCallback((_, args) =>
            DynValue.NewNumber(f(ArgNum(args, 0, sig))));

    static DynValue Bin(Func<double, double, double> f, string sig)
        => DynValue.NewCallback((_, args) =>
        {
            var a = ArgNum(args, 0, sig);
            var b = ArgNum(args, 1, sig);
            return DynValue.NewNumber(f(a, b));
        });
    static double ArgNum(CallbackArguments args, int index, string sig)
    {
        if (args.Count <= index || args[index].Type != DataType.Number)
            throw new ScriptRuntimeException($"{sig}: 参数需要数字");

        var d = args[index].Number;
        if (double.IsNaN(d) || double.IsInfinity(d))
            throw new ScriptRuntimeException($"{sig}: 不能是 NaN / 无穷");

        return d;
    }
    static double NumOf(Table t, string key)
    {
        var v = t.Get(key);
        if (v.Type == DataType.Nil || v.Type == DataType.Void)
            return 0;
        if (v.Type != DataType.Number)
            throw new ScriptRuntimeException($"timespan.of: 字段 '{key}' 需要数字");
        return v.Number;
    }
    static string Humanize(TimeSpan span)
    {
        var neg = span.Ticks < 0;
        span = span.Duration();

        var text = span.Days > 0
            ? $"{span.Days}d {span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}"
            : span.Hours > 0
                ? $"{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}"
                : $"{span.Minutes:00}:{span.Seconds:00}";

        return neg ? "-" + text : text;
    }
}
