using Mirror;

using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Time;

/// <summary>
/// time 表（Mirror 网络时间 + Unity 本地时间）：
///
///   time.now()      -> number    Mirror 网络时间（秒，double）
///                                服务端/客户端一致，用于同步逻辑
///                                不要转 float，会丢精度
///
///   time.rtt()      -> number    往返延迟（秒）
///
///   time.local()    -> number    本地已运行时间（秒，忽略 timeScale）
///
///   time.delta()    -> number    上一帧间隔（秒，忽略 timeScale）
/// </summary>
internal class LuaNetworkTimeProxy : IRegisterLua
{
    public string Name { get; } = "time";

    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);

        t["now"] = DynValue.NewCallback((_, _) =>
        {
            return DynValue.NewNumber(NetworkTime.time);
        });

        t["rtt"] = DynValue.NewCallback((_, _) =>
        {
            return DynValue.NewNumber(NetworkTime.rtt);
        });

        t["local"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(UnityEngine.Time.unscaledTimeAsDouble));

        t["delta"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(UnityEngine.Time.unscaledDeltaTime));

        return t;
    }
}
