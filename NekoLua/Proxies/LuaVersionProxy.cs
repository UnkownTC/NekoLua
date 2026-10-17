using MoonSharp.Interpreter;

using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies;

/// <summary>
/// version 表：
///   version.new(major[,minor[,build[,revision]]])
///   version.parse(str)              -> table | nil
///   version.zero
///   version.tostring(v[, fields])   -- fields: 1/2/3/4
///   version.major / minor / build / revision(v)
///   version.compare(a, b)           -> -1 / 0 / 1
///   version.equals / greater / less / greaterOrEqual / lessOrEqual(a, b)
///   version.isAtLeast(v, major[,minor[,build[,revision]]])
///   version.inRange(v, min, max)
/// </summary>
internal class LuaVersionProxy : IRegisterLua
{
    public string Name { get; } = "version";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["new"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException(
                    "version.new(major[, minor[, build[, revision]]])");

            var major = (int)args[0].Number;
            var minor = OptInt(args, 1, 0);
            var build = OptInt(args, 2, 0);
            var rev = OptInt(args, 3, 0);

            return Pack(s, new Version(major, minor, build, rev));
        });

        t["parse"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("version.parse(str)");

            return Version.TryParse(args[0].String, out var v)
                ? Pack(s, v)
                : DynValue.Nil;
        });

        t["zero"] = Pack(s, new Version(0, 0, 0, 0));

        t["tostring"] = DynValue.NewCallback((_, args) =>
        {
            var v = Unpack(args, 0, "version.tostring(v[, fields])");
            var n = args.Count > 1 && args[1].Type == DataType.Number
                ? (int)args[1].Number
                : 4;

            var str = n switch
            {
                <= 1 => $"{v.Major}",
                2 => $"{v.Major}.{v.Minor}",
                3 => $"{v.Major}.{v.Minor}.{v.Build}",
                _ => v.ToString(),
            };
            return DynValue.NewString(str);
        });

        t["major"] = Comp(v => v.Major);
        t["minor"] = Comp(v => v.Minor);
        t["build"] = Comp(v => v.Build);
        t["revision"] = Comp(v => v.Revision);

        t["compare"] = DynValue.NewCallback((_, args) =>
        {
            var a = Unpack(args, 0, "version.compare(a, b)");
            var b = Unpack(args, 1, "version.compare(a, b)");
            return DynValue.NewNumber(Math.Sign(a.CompareTo(b)));
        });

        t["equals"] = Cmp((a, b) => a == b, "version.equals(a, b)");
        t["greater"] = Cmp((a, b) => a > b, "version.greater(a, b)");
        t["less"] = Cmp((a, b) => a < b, "version.less(a, b)");
        t["greaterOrEqual"] = Cmp((a, b) => a >= b, "version.greaterOrEqual(a, b)");
        t["lessOrEqual"] = Cmp((a, b) => a <= b, "version.lessOrEqual(a, b)");

        t["isAtLeast"] = DynValue.NewCallback((_, args) =>
        {
            var v = Unpack(args, 0, "version.isAtLeast(v, major[, minor[, build[, revision]]])");
            if (args.Count < 2 || args[1].Type != DataType.Number)
                throw new ScriptRuntimeException(
                    "version.isAtLeast(v, major[, minor[, build[, revision]]])");

            var major = (int)args[1].Number;
            var minor = OptInt(args, 2, 0);
            var build = OptInt(args, 3, 0);
            var rev = OptInt(args, 4, 0);

            return DynValue.NewBoolean(v >= new Version(major, minor, build, rev));
        });

        t["inRange"] = DynValue.NewCallback((_, args) =>
        {
            var v = Unpack(args, 0, "version.inRange(v, min, max)");
            var min = Unpack(args, 1, "version.inRange(v, min, max)");
            var max = Unpack(args, 2, "version.inRange(v, min, max)");
            return DynValue.NewBoolean(v >= min && v <= max);
        });

        return t;
    }
    static DynValue Pack(Script script, Version v) => DynValue.NewTable(new Table(script)
    {
        ["major"] = v.Major,
        ["minor"] = v.Minor,
        ["build"] = v.Build,
        ["revision"] = v.Revision,
    });
    static Version Unpack(CallbackArguments args, int index, string sig)
    {
        if (args.Count <= index)
            throw new ScriptRuntimeException(sig);

        var v = args[index];

        if (v.Type == DataType.String)
        {
            if (!Version.TryParse(v.String, out var parsed))
                throw new ScriptRuntimeException($"{sig}: 非法版本号 '{v.String}'");
            return parsed;
        }

        if (v.Type == DataType.Table)
        {
            var t = v.Table;
            return new Version(
                (int)t.Get("major").Number,
                (int)t.Get("minor").Number,
                (int)t.Get("build").Number,
                (int)t.Get("revision").Number);
        }

        throw new ScriptRuntimeException($"{sig}: 需要版本 table 或字符串");
    }
    static int OptInt(CallbackArguments args, int index, int fallback)
        => args.Count > index && args[index].Type == DataType.Number
            ? (int)args[index].Number
            : fallback;

    static DynValue Comp(Func<Version, int> f)
        => DynValue.NewCallback((_, args) =>
        {
            var v = Unpack(args, 0, "version.<component>(v)");
            return DynValue.NewNumber(f(v));
        });

    static DynValue Cmp(Func<Version, Version, bool> f, string sig)
        => DynValue.NewCallback((_, args) =>
        {
            var a = Unpack(args, 0, sig);
            var b = Unpack(args, 1, sig);
            return DynValue.NewBoolean(f(a, b));
        });
}
