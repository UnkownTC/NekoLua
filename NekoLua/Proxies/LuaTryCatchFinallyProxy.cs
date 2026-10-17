using MoonSharp.Interpreter;

using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies;

/// <summary>
/// try 表：
///   try.run(tryFn[, catchFn[, finallyFn]])
///     -- 返回 (true, tryFn 或 catchFn 的返回值...)
///     -- catch 缺失时异常向上抛；finally 抛异常覆盖一切
///   try.attempt(fn)
///     -- 不抛异常版：返回 (true, ...) 或 (false, errTable)
///
/// errTable = { message, trace, type }
///   type: "syntax" / "runtime" / "interpreter" / "unknown"
/// </summary>
internal class LuaTryCatchFinallyProxy : IRegisterLua
{
    public string Name { get; } = "try";
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["run"] = DynValue.NewCallback((_, args) => Run(s, args));
        t["attempt"] = DynValue.NewCallback((_, args) => Attempt(s, args));

        return t;
    }
    static DynValue Run(Script script, CallbackArguments args)
    {
        if (args.Count < 1 || args[0].Type != DataType.Function)
            throw new ScriptRuntimeException("try.run(tryFn[, catchFn[, finallyFn]])");

        var tryFn = args[0];
        var catchFn = args.Count > 1 && args[1].Type == DataType.Function ? args[1] : null;
        var finallyFn = args.Count > 2 && args[2].Type == DataType.Function ? args[2] : null;

        DynValue? result = null;
        Exception? pendingException = null;

        try
        {
            result = tryFn.Function.Call();
        }
        catch (Exception ex)
        {
            pendingException = ex;
        }
        if (pendingException != null && catchFn != null)
        {
            try
            {
                result = catchFn.Function.Call(BuildErrorTable(script, pendingException));
                pendingException = null;
            }
            catch (Exception ex)
            {
                pendingException = ex;
                result = null;
            }
        }
        if (finallyFn != null)
        {
            try
            {
                _ = finallyFn.Function.Call();
            }
            catch (Exception ex)
            {
                pendingException = ex;
                result = null;
            }
        }
        if (pendingException != null)
            Rethrow(pendingException);

        return PackSuccess(result ?? DynValue.Nil);
    }
    static DynValue Attempt(Script script, CallbackArguments args)
    {
        if (args.Count < 1 || args[0].Type != DataType.Function)
            throw new ScriptRuntimeException("try.attempt(fn)");

        try
        {
            var result = args[0].Function.Call();
            return PackSuccess(result);
        }
        catch (Exception ex)
        {
            return DynValue.NewTuple(
                DynValue.False,
                BuildErrorTable(script, ex));
        }
    }
    static DynValue PackSuccess(DynValue result)
    {
        if (result.Type == DataType.Tuple)
        {
            var items = result.Tuple;
            var all = new DynValue[items.Length + 1];
            all[0] = DynValue.True;
            Array.Copy(items, 0, all, 1, items.Length);
            return DynValue.NewTuple(all);
        }

        return DynValue.NewTuple(DynValue.True, result);
    }
    static DynValue BuildErrorTable(Script script, Exception ex)
    {
        var message = ex.Message;
        var trace = ex is InterpreterException iex ? iex.DecoratedMessage : message;
        var type = ex switch
        {
            SyntaxErrorException => "syntax",
            ScriptRuntimeException => "runtime",
            InterpreterException => "interpreter",
            _ => "unknown",
        };

        return DynValue.NewTable(new Table(script)
        {
            ["message"] = message,
            ["trace"] = trace,
            ["type"] = type,
        });
    }
    static void Rethrow(Exception ex)
    {
        if (ex is InterpreterException ie)
            throw ie;
        throw new ScriptRuntimeException(ex.Message);
    }
}
