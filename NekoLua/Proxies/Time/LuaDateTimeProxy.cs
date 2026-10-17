using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;
using NorthwoodLib.Pools;
using System.Globalization;

namespace NekoLua.Proxies.Time;

/// <summary>
/// datetime 表：
///   datetime.now()                  -- Unix 秒（double）
///   datetime.millis()               -- Unix 毫秒
///   datetime.format(fmt[, t])       -- strftime，"!" 前缀 = UTC
///   datetime.parse(str, fmt)        -> Unix 秒
///   datetime.parts([t])             -> {year, month, day, hour, min, sec, wday, yday}
///   datetime.diff(a, b)             -- a - b
///   datetime.utcOffset()            -- 本地时区偏移（秒）
/// </summary>
internal class LuaDateTimeProxy : IRegisterLua
{
    public string Name { get; } = "datetime";

    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var t = new Table(script);

        t["now"] = DynValue.NewCallback((_, _) => DynValue.NewNumber(NowSeconds()));
        t["millis"] = DynValue.NewCallback((_, _) => DynValue.NewNumber(NowMillis()));

        t["format"] = DynValue.NewCallback((_, args) => Format(args));
        t["parse"] = DynValue.NewCallback((_, args) => Parse(args));
        t["parts"] = DynValue.NewCallback((_, args) => Parts(script, args));
        t["diff"] = DynValue.NewCallback((_, args) => Diff(args));

        t["utcOffset"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalSeconds));

        return t;
    }
    static double NowSeconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    }

    static double NowMillis()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    static DynValue Format(CallbackArguments args)
    {
        if (args.Count < 1 || args[0].Type != DataType.String)
            throw new ScriptRuntimeException("datetime.format(fmt, t?)");

        var fmt = args[0].String;
        var utc = false;

        if (fmt.StartsWith("!"))
        {
            utc = true;
            fmt = fmt.Substring(1);
        }

        var ts = args.Count > 1 && args[1].Type == DataType.Number
            ? args[1].Number
            : NowSeconds();

        var dto = DateTimeOffset.FromUnixTimeMilliseconds((long)(ts * 1000));
        if (!utc)
            dto = dto.ToLocalTime();

        var netFmt = ToDotNetFormat(fmt);
        return DynValue.NewString(dto.ToString(netFmt, CultureInfo.InvariantCulture));
    }
    static DynValue Parse(CallbackArguments args)
    {
        if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.String)
            throw new ScriptRuntimeException("datetime.parse(str, fmt)");

        var str = args[0].String;
        var fmt = ToDotNetFormat(args[1].String);

        return !DateTimeOffset.TryParseExact(str, fmt, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var dto)
            ? throw new ScriptRuntimeException($"datetime.parse: 无法解析 '{str}'")
            : DynValue.NewNumber(dto.ToUnixTimeMilliseconds() / 1000.0);
    }
    static DynValue Parts(Script script, CallbackArguments args)
    {
        var ts = args.Count > 0 && args[0].Type == DataType.Number
            ? args[0].Number
            : NowSeconds();

        var dt = DateTimeOffset.FromUnixTimeMilliseconds((long)(ts * 1000))
            .ToLocalTime().DateTime;

        var t = new Table(script)
        {
            ["year"] = dt.Year,
            ["month"] = dt.Month,
            ["day"] = dt.Day,
            ["hour"] = dt.Hour,
            ["min"] = dt.Minute,
            ["sec"] = dt.Second,
            ["wday"] = (int)dt.DayOfWeek + 1,
            ["yday"] = dt.DayOfYear,
        };
        return DynValue.NewTable(t);
    }
    static DynValue Diff(CallbackArguments args)
    {
        return args.Count < 2 || args[0].Type != DataType.Number || args[1].Type != DataType.Number
            ? throw new ScriptRuntimeException("datetime.diff(a, b)")
            : DynValue.NewNumber(args[0].Number - args[1].Number);
    }
    private static readonly Dictionary<string, string> _fmtCache = new(StringComparer.Ordinal);
    static string ToDotNetFormat(string fmt)
    {
        if (_fmtCache.TryGetValue(fmt, out var cached))
            return cached;

        var sb = StringBuilderPool.Shared.Rent(fmt.Length);
        for (var i = 0; i < fmt.Length; i++)
        {
            if (fmt[i] != '%' || i + 1 >= fmt.Length)
            {
                _ = sb.Append(fmt[i]);
                continue;
            }

            var c = fmt[++i];
            _ = sb.Append(c switch
            {
                'Y' => "yyyy",
                'y' => "yy",
                'm' => "MM",
                'd' => "dd",
                'H' => "HH",
                'I' => "hh",
                'M' => "mm",
                'S' => "ss",
                'p' => "tt",
                'A' => "dddd",
                'a' => "ddd",
                'B' => "MMMM",
                'b' => "MMM",
                'z' => "zzz",
                '%' => "%",
                _ => "%" + c,
            });
        }

        var result = StringBuilderPool.Shared.ToStringReturn(sb);

        if (_fmtCache.Count > 256)
            _fmtCache.Clear();
        _fmtCache[fmt] = result;
        return result;
    }
}
