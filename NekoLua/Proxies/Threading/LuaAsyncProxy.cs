using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Threading;

/// <summary>
/// async 表：
///   async.run(fn) / async(fn)       -- 启动协程
///   async.wait(seconds)             -- 只能在协程里调，让出
///   async.count()                   -- 本 mod 活跃协程数
/// </summary>
public class LuaAsyncProxy : IRegisterLua
{
    public string Name { get; } = "async";
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        var runFn = DynValue.NewCallback((_, args) => Run(s, mod, args));
        t["run"] = runFn;
        t["spawn"] = runFn;

        var waitFn = DynValue.NewCallback((_, args) => Wait(s, args));
        t["wait"] = waitFn;
        t["sleep"] = waitFn;

        var meta = new Table(s);
        meta["__call"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2)
                throw new ScriptRuntimeException("async(fn): 需要函数");

            var inner = new CallbackArguments(
                [args[1]], isMethodCall: false);
            return Run(s, mod, inner);
        });
        t.MetaTable = meta;

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LuaAsyncScheduler.CountOf(mod)));

        return t;
    }
    private static DynValue Run(Script script, LuaModInstance mod, CallbackArguments args)
    {
        if (args.Count < 1 || args[0].Type != DataType.Function)
            throw new ScriptRuntimeException("async.run(fn): 需要函数");

        var co = script.CreateCoroutine(args[0]).Coroutine;
        LuaAsyncScheduler.Schedule(co, 0, mod);
        return DynValue.Nil;
    }
    private static DynValue Wait(Script script, CallbackArguments args)
    {
        if (args.Count < 1 || args[0].Type != DataType.Number)
            throw new ScriptRuntimeException("async.wait(seconds): 需要数字");

        var seconds = args[0].Number;
        if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
            throw new ScriptRuntimeException("async.wait: 秒数必须是非负有限数值");

        var waitTbl = new Table(script);
        waitTbl["__wait"] = seconds;

        var coroutineTbl = script.Globals.Get("coroutine");
        if (coroutineTbl.Type != DataType.Table)
            throw new ScriptRuntimeException("async.wait: 无法访问 coroutine 库");

        var yieldFn = coroutineTbl.Table.Get("yield");
        return yieldFn.Type != DataType.Function
            ? throw new ScriptRuntimeException("async.wait: 无法访问 coroutine.yield")
            : yieldFn.Function.Call(DynValue.NewTable(waitTbl));
    }
}
