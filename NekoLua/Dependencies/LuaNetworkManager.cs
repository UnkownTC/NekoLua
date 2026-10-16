using NekoLua.Loader;

namespace NekoLua.Dependencies;

public static class LuaNetworkManager
{
    // _byMod 会被 IO 线程（连接失败 → Dispose → Unregister）与主线程（create → TryRegister）
    // 并发访问，Dictionary 非线程安全，桶链损坏可致主线程死循环（审查 P2-4）
    private static readonly object _gate = new();
    private static readonly Dictionary<LuaModInstance, List<IDisposable>> _byMod = [];
    public static bool TryRegister(LuaModInstance mod, IDisposable conn)
    {
        lock (_gate)
        {
            if (!_byMod.TryGetValue(mod, out var list))
                _byMod[mod] = list = [];

            var max = MainPlugin.CacheConfig.MaxConnectionsPerMod;
            if (list.Count >= max)
                return false;

            list.Add(conn);
            return true;
        }
    }
    public static void Unregister(LuaModInstance mod, IDisposable conn)
    {
        lock (_gate)
        {
            if (_byMod.TryGetValue(mod, out var list))
                _ = list.Remove(conn);
        }
    }
    public static int CountOf(LuaModInstance mod)
    {
        lock (_gate)
        {
            return _byMod.TryGetValue(mod, out var list) ? list.Count : 0;
        }
    }

    public static void CleanupFor(LuaModInstance mod)
    {
        IDisposable[] conns;
        lock (_gate)
        {
            if (!_byMod.TryGetValue(mod, out var list))
                return;

            conns = [.. list];
            _ = _byMod.Remove(mod);
        }

        foreach (var conn in conns)
        {
            try
            { conn.Dispose(); }
            catch { }
        }
    }
    public static void ClearAll()
    {
        List<List<IDisposable>> lists;
        lock (_gate)
        {
            lists = [.. _byMod.Values];
            _byMod.Clear();
        }

        foreach (var list in lists)
            foreach (var conn in list.ToArray())
                try
                { conn.Dispose(); }
                catch { }
    }
}
