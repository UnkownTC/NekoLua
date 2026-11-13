using LabApi.Features.Console;

using MoonSharp.Interpreter;

using NekoLua.Loader;

namespace NekoLua.Dependencies;

public static class LuaEventBus
{
    private const int MaxRecursionDepth = 16;
    private sealed class Sub
    {
        public required string Topic;
        public required string Id;
        public required DynValue Callback;
        public required LuaModInstance Owner;
        public bool Once;
        public bool Cancelled;
    }
    private static readonly List<Sub> _subs = [];
    private static int _depth;
    public static void On(string topic, string id, DynValue cb, LuaModInstance owner, bool once)
    {
        _subs.Add(new Sub
        {
            Topic = topic,
            Id = id,
            Callback = cb,
            Owner = owner,
            Once = once,
        });
    }
    public static int Off(string topic, string id, LuaModInstance owner)
    {
        var n = 0;
        foreach (var s in _subs)
        {
            if (s.Cancelled)
                continue;
            if (!ReferenceEquals(s.Owner, owner))
                continue;
            if (s.Topic != topic)
                continue;
            if (id != "*" && s.Id != id)
                continue;
            s.Cancelled = true;
            n++;
        }
        _ = _subs.RemoveAll(s => s.Cancelled);
        return n;
    }
    public static int OffAll(LuaModInstance owner)
    {
        var n = 0;
        foreach (var s in _subs)
        {
            if (s.Cancelled)
                continue;
            if (!ReferenceEquals(s.Owner, owner))
                continue;
            s.Cancelled = true;
            n++;
        }
        _ = _subs.RemoveAll(s => s.Cancelled);
        return n;
    }
    public static int Count(LuaModInstance owner)
    {
        var n = 0;
        foreach (var s in _subs)
            if (!s.Cancelled && ReferenceEquals(s.Owner, owner))
                n++;
        return n;
    }
    public static List<(string Topic, string Id)> List(LuaModInstance owner)
    {
        var result = new List<(string, string)>();
        foreach (var s in _subs)
            if (!s.Cancelled && ReferenceEquals(s.Owner, owner))
                result.Add((s.Topic, s.Id));
        return result;
    }
    public static int Emit(string topic, DynValue payload, LuaModInstance source)
    {
        if (_depth >= MaxRecursionDepth)
        {
            Logger.Warn($"[{source.Name}] eventbus.emit 递归过深（>{MaxRecursionDepth}），已忽略 topic='{topic}'");
            return 0;
        }

        string payloadJson;
        try
        {
            payloadJson = LuaJson.ToJson(payload);
        }
        catch (Exception ex)
        {
            Logger.Error($"[{source.Name}] eventbus.emit 序列化失败: {ex.Message}");
            return 0;
        }

        var snapshot = _subs.Where(s => !s.Cancelled && s.Topic == topic).ToArray();

        _depth++;
        try
        {
            var delivered = 0;

            foreach (var s in snapshot)
            {
                if (s.Cancelled)
                    continue;
                DynValue arg;
                try
                {
                    arg = LuaJson.FromJson(s.Owner.Script, payloadJson);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[{s.Owner.Name}] eventbus 反序列化失败: {ex.Message}");
                    continue;
                }
                var caller = DynValue.NewString(source.Name);

                try
                {
                    _ = s.Callback.Function.Call(arg, caller);
                    delivered++;
                }
                catch (InterpreterException ex)
                {
                    Logger.Error($"[{s.Owner.Name}] eventbus 回调异常 (topic={topic}, id={s.Id}): {ex.DecoratedMessage}");
                }
                catch (Exception ex)
                {
                    Logger.Error($"[{s.Owner.Name}] eventbus 回调异常: {ex.Message}");
                }

                if (s.Once)
                    s.Cancelled = true;
            }

            _ = _subs.RemoveAll(s => s.Cancelled);
            return delivered;
        }
        finally
        {
            _depth--;
        }
    }
    public static void CleanupFor(LuaModInstance mod)
    {
        foreach (var s in _subs)
            if (ReferenceEquals(s.Owner, mod))
                s.Cancelled = true;

        _ = _subs.RemoveAll(s => s.Cancelled);
    }
    public static void ClearAll()
    {
        _subs.Clear();
        _depth = 0;
    }
}
