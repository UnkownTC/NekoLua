using MoonSharp.Interpreter;
using NekoLua.Api;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies;

/// <summary>
/// eventbus 表（跨 mod，payload 走 JSON）：
///   eventbus.emit(topic, payload)   -> number 送达数
///   eventbus.on(topic, id, callback)          -- callback(payload, sender)
///   eventbus.once(topic, id, callback)
///   eventbus.off(topic, id)         -> number
///   eventbus.offAll()               -> number
///   eventbus.count() / list()
/// </summary>
internal class LuaEventBusProxy : IRegisterLua
{
    public string Name { get; } = "eventbus";
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["emit"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("eventbus.emit(topic, payload)");

            var delivered = LuaEventBus.Emit(args[0].String, args[1], mod);
            return DynValue.NewNumber(delivered);
        });

        t["on"] = RegisterCallback(mod, once: false);
        t["once"] = RegisterCallback(mod, once: true);

        t["off"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.String)
                throw new ScriptRuntimeException("eventbus.off(topic, id)");

            return DynValue.NewNumber(LuaEventBus.Off(args[0].String, args[1].String, mod));
        });

        t["offAll"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LuaEventBus.OffAll(mod)));

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LuaEventBus.Count(mod)));

        t["list"] = DynValue.NewCallback((_, _) =>
        {
            var list = LuaEventBus.List(mod);
            var result = new Table(s);
            var i = 1;
            foreach (var (topic, id) in list)
            {
                var entry = new Table(s) { ["topic"] = topic, ["id"] = id };
                result[i++] = DynValue.NewTable(entry);
            }
            return DynValue.NewTable(result);
        });

        return t;
    }
    static DynValue RegisterCallback(LuaModInstance mod, bool once)
    {
        var name = once ? "once" : "on";
        return DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 3 ||
                args[0].Type != DataType.String ||
                args[1].Type != DataType.String ||
                args[2].Type != DataType.Function)
                throw new ScriptRuntimeException($"eventbus.{name}(topic, id, callback)");

            LuaEventBus.On(args[0].String, args[1].String, args[2], mod, once);
            return DynValue.Nil;
        });
    }
}
