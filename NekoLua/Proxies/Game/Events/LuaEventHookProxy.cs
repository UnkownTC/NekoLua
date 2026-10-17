using MoonSharp.Interpreter;
using NekoLua.Api;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Game.Events;

/// <summary>
/// eventhook 表：
///   eventhook.bind(className, eventName, id, callback)
///   eventhook.unbind(id)            -> number
///   eventhook.unbindAll()           -> number
///   eventhook.count()               -> number
///   eventhook.classes()             -> table     可用事件类
///   eventhook.events(className)     -> table     类下所有事件名
/// </summary>
internal class LuaEventHookProxy : IRegisterLua
{
    public string Name { get; } = "eventhook";
    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);
        var hook = new LuaEventHook(mod);

        t["bind"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 4 ||
                args[0].Type != DataType.String ||
                args[1].Type != DataType.String ||
                args[2].Type != DataType.String ||
                args[3].Type != DataType.Function)
                throw new ScriptRuntimeException(
                    $"{Name}.bind(className, eventName, id, callback)");

            hook.Bind(args[0].String, args[1].String, args[2].String, args[3]);
            return DynValue.Nil;
        });

        t["unbind"] = DynValue.NewCallback((_, args) =>
        {
            return args.Count < 1 || args[0].Type != DataType.String
                ? throw new ScriptRuntimeException($"{Name}.unbind(id)")
                : DynValue.NewNumber(hook.Unbind(args[0].String));
        });

        t["unbindAll"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(hook.UnbindAll()));

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(hook.Count));

        t["classes"] = DynValue.NewCallback((_, _) =>
        {
            var tbl = new Table(mod.Script);
            var i = 1;
            foreach (var name in LuaEventHook.EventClasses.Keys.OrderBy(k => k))
                tbl[i++] = DynValue.NewString(name);
            return DynValue.NewTable(tbl);
        });

        t["events"] = DynValue.NewCallback((_, args) =>
        {
            return args.Count < 1 || args[0].Type != DataType.String
                ? throw new ScriptRuntimeException($"{Name}.events(className)")
                : DynValue.NewTable(LuaEventHook.ListEvents(mod.Script, args[0].String));
        });

        return t;
    }
}
