using System.Linq.Expressions;
using System.Reflection;

using LabApi.Features.Console;

using MoonSharp.Interpreter;

using NekoLua.Loader;

namespace NekoLua.Dependencies;

public sealed class LuaEventHook
{
    private static readonly List<LuaEventHook> _all = [];

    public static readonly Dictionary<string, Type> EventClasses = DiscoverEventClasses();
    private sealed class Binding
    {
        public required string Id;
        public required string ClassName;
        public required string EventName;
        public required EventInfo Event;
        public required Delegate Handler;
        public required DynValue Callback;
    }
    private readonly LuaModInstance _mod;
    private readonly Dictionary<string, List<Binding>> _byId = new(StringComparer.Ordinal);

    public LuaEventHook(LuaModInstance mod)
    {
        _mod = mod;
        _all.Add(this);
    }

    public int Count
    {
        get
        {
            var n = 0;
            foreach (var list in _byId.Values)
                n += list.Count;
            return n;
        }
    }
    public void Bind(string className, string eventName, string id, DynValue callback)
    {
        if (!EventClasses.TryGetValue(className, out var type))
            throw new ScriptRuntimeException(
                $"未知事件类 '{className}'，可用: {string.Join(", ", EventClasses.Keys)}");

        var ev = FindEvent(type, eventName)
            ?? throw new ScriptRuntimeException(
                $"事件类 '{className}' 里没有事件 '{eventName}'");

        var binding = new Binding
        {
            Id = id,
            ClassName = className,
            EventName = ev.Name,
            Event = ev,
            Callback = callback,
            Handler = null!,
        };
        binding.Handler = CreateHandler(ev, binding);

        ev.AddEventHandler(null, binding.Handler);

        if (!_byId.TryGetValue(id, out var list))
            _byId[id] = list = [];

        list.Add(binding);
    }

    public int Unbind(string id)
    {
        if (!_byId.TryGetValue(id, out var list))
            return 0;

        foreach (var b in list)
        {
            try
            { b.Event.RemoveEventHandler(null, b.Handler); }
            catch (Exception ex)
            {
                Logger.Warn($"[{_mod.Name}] 反注册 {b.EventName} 失败: {ex.Message}");
            }
        }

        _byId.Remove(id);
        return list.Count;
    }

    public int UnbindAll()
    {
        var total = 0;
        foreach (var id in _byId.Keys.ToArray())
            total += Unbind(id);
        return total;
    }
    public static void CleanupFor(LuaModInstance mod)
    {
        foreach (var h in _all.Where(h => ReferenceEquals(h._mod, mod)).ToArray())
        {
            h.UnbindAll();
            _all.Remove(h);
        }
    }
    public static void ClearAll()
    {
        foreach (var h in _all)
            h.UnbindAll();
        _all.Clear();
    }
    private Delegate CreateHandler(EventInfo ev, Binding binding)
    {
        var handlerType = ev.EventHandlerType!;
        var invoke = handlerType.GetMethod("Invoke")!;
        var parameters = invoke.GetParameters();

        var paramExprs = parameters
            .Select(p => Expression.Parameter(p.ParameterType, p.Name))
            .ToArray();

        var argsArray = Expression.NewArrayInit(
            typeof(object),
            paramExprs.Select(p => Expression.Convert(p, typeof(object))));

        var invokeMethod = typeof(LuaEventHook)
            .GetMethod(nameof(InvokeCallback), BindingFlags.NonPublic | BindingFlags.Instance)!;

        var body = Expression.Call(
            Expression.Constant(this),
            invokeMethod,
            Expression.Constant(binding),
            argsArray);

        return Expression.Lambda(handlerType, body, paramExprs).Compile();
    }

    private void InvokeCallback(Binding binding, object[] args)
    {
        try
        {
            DynValue[] luaArgs = args.Length switch
            {
                0 => [],
                1 => [BuildEventArgs(args[0])],
                _ => [.. args.Select(a => BuildEventArgs(a))],
            };

            binding.Callback.Function.Call(luaArgs);
        }
        catch (InterpreterException ex)
        {
            Logger.Error($"[{_mod.Name}] 事件 {binding.EventName} 回调异常: {ex.DecoratedMessage}");
        }
        catch (Exception ex)
        {
            Logger.Error($"[{_mod.Name}] 事件 {binding.EventName} 回调异常: {ex.Message}");
        }
    }
    private DynValue BuildEventArgs(object? ev)
    {
        if (ev is null)
            return DynValue.Nil;

        return DynValue.NewTable(
            new LuaTypeMapper(_mod.Script)
                .UseProxy()
                .ScanInstance(ev)
                .Build());
    }
    private static EventInfo? FindEvent(Type type, string name)
    {
        foreach (var ev in type.GetEvents(BindingFlags.Public | BindingFlags.Static))
            if (string.Equals(ev.Name, name, StringComparison.OrdinalIgnoreCase))
                return ev;
        return null;
    }
    private static Dictionary<string, Type> DiscoverEventClasses()
    {
        var result = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        var asm = typeof(LabApi.Events.Handlers.ServerEvents).Assembly;

        foreach (var type in asm.GetTypes())
        {
            if (!type.IsAbstract || !type.IsSealed)
                continue;
            if (type.Namespace != "LabApi.Events.Handlers")
                continue;
            if (!type.Name.EndsWith("Events"))
                continue;

            var baseName = type.Name.Substring(0, type.Name.Length - "Events".Length);
            var luaName = char.ToLowerInvariant(baseName[0])
                         + baseName.Substring(1).ToLowerInvariant();

            result[luaName] = type;
        }

        return result;
    }
    public static Table ListEvents(Script script, string className)
    {
        if (!EventClasses.TryGetValue(className, out var type))
            throw new ScriptRuntimeException(
                $"未知事件类 '{className}'，可用: {string.Join(", ", EventClasses.Keys)}");

        var t = new Table(script);
        var i = 1;
        foreach (var ev in type.GetEvents(BindingFlags.Public | BindingFlags.Static)
                               .OrderBy(e => e.Name))
            t[i++] = DynValue.NewString(ev.Name);
        return t;
    }
}
