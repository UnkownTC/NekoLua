using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Time;

/// <summary>
/// timer 表：
///   timer.delay(seconds[, id], callback)      -- 延时一次
///   timer.every(seconds[, id], callback)      -- 先触发再等待
///   timer.everyLate(seconds[, id], callback)  -- 先等待再触发
///   timer.cancel(id) / cancelAll() / count()
/// </summary>
public class LuaTimerProxy : IRegisterLua
{
    public string Name { get; } = "timer";
    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);

        t["delay"] = DynValue.NewCallback((_, args) =>
        {
            var (s, id, cb) = Parse(args, "timer.delay(seconds, [id], callback)");
            LuaTimer.Delay(s, id, cb, mod);
            return DynValue.Nil;
        });

        t["every"] = DynValue.NewCallback((_, args) =>
        {
            var (s, id, cb) = Parse(args, "timer.every(seconds, [id], callback)");
            LuaTimer.Every(s, id, cb, mod);
            return DynValue.Nil;
        });

        t["everyLate"] = DynValue.NewCallback((_, args) =>
        {
            var (s, id, cb) = Parse(args, "timer.everyLate(seconds, [id], callback)");
            LuaTimer.EveryLate(s, id, cb, mod);
            return DynValue.Nil;
        });

        t["cancel"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || (args[0].Type != DataType.String && args[0].Type != DataType.Number))
                throw new ScriptRuntimeException("timer.cancel(id): id 需要字符串或数字");

            var id = args[0].Type == DataType.String
                ? (object)args[0].String
                : args[0].Number;

            return DynValue.NewNumber(LuaTimer.Cancel(id, mod));
        });

        t["cancelAll"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LuaTimer.CancelAll(mod)));

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LuaTimer.Count(mod)));

        return t;
    }
    static (double seconds, object? id, DynValue cb) Parse(CallbackArguments args, string sig)
    {
        if (args.Count < 2 || args[0].Type != DataType.Number)
            throw new ScriptRuntimeException(sig);

        var seconds = args[0].Number;
        if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            throw new ScriptRuntimeException($"{sig}: 秒数必须是非负有限数值");

        if (args[1].Type == DataType.Function)
            return (seconds, null, args[1]);

        if (args[1].Type == DataType.String || args[1].Type == DataType.Number)
        {
            if (args.Count < 3 || args[2].Type != DataType.Function)
                throw new ScriptRuntimeException(sig);

            object id = args[1].Type == DataType.String
                ? args[1].String
                : args[1].Number;

            if (id is string s && string.IsNullOrWhiteSpace(s))
                throw new ScriptRuntimeException($"{sig}: id 不能为空");

            return (seconds, id, args[2]);
        }

        throw new ScriptRuntimeException(sig);
    }
}
