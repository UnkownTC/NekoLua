using LabApi.Features.Console;

using MoonSharp.Interpreter;
using NekoLua.Loader;

namespace NekoLua.Api;

internal static class LuaAsyncScheduler
{
    private sealed class WaitCo
    {
        public required Coroutine Co;
        public required LuaModInstance Owner;
        public double Remaining;
    }
    private static readonly List<WaitCo> _waiting = [];
    public static void Schedule(Coroutine co, double delay, LuaModInstance owner)
    {
        _waiting.Add(new WaitCo
        {
            Co = co,
            Owner = owner,
            Remaining = Math.Max(0, delay),
        });
    }
    public static void Tick(double deltaTime)
    {
        if (deltaTime <= 0 || _waiting.Count == 0)
            return;

        for (var i = _waiting.Count - 1; i >= 0; i--)
        {
            var w = _waiting[i];
            w.Remaining -= deltaTime;
            if (w.Remaining > 0)
                continue;

            _waiting.RemoveAt(i);
            Resume(w);
        }
    }
    private static void Resume(WaitCo w)
    {
        try
        {
            var result = w.Co.Resume();

            if (w.Co.State == CoroutineState.Dead)
                return;

            if (result.Type == DataType.Table)
            {
                var wt = result.Table.Get("__wait");
                if (wt.Type == DataType.Number)
                {
                    _waiting.Add(new WaitCo
                    {
                        Co = w.Co,
                        Owner = w.Owner,
                        Remaining = Math.Max(0, wt.Number),
                    });
                }
            }
        }
        catch (InterpreterException ex)
        {
            Logger.Error($"[{w.Owner.Name}] async 协程异常: {ex.DecoratedMessage}");
        }
        catch (Exception ex)
        {
            Logger.Error($"[{w.Owner.Name}] async 协程异常: {ex.Message}");
        }
    }
    public static int CountOf(LuaModInstance owner)
    {
        var n = 0;
        foreach (var w in _waiting)
            if (ReferenceEquals(w.Owner, owner))
                n++;
        return n;
    }
    public static void CleanupFor(LuaModInstance mod)
    {
        _ = _waiting.RemoveAll(w => ReferenceEquals(w.Owner, mod));
    }

    public static void ClearAll()
    {
        _waiting.Clear();
    }
}
