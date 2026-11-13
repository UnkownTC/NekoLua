using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using Respawning.Waves;

namespace NekoLua.Proxies.GameContents;

/// <summary>
/// respawn 表（对应 LabAPI <c>Facility/Respawning/*</c>）：
///
///   波次句柄（无波次时为 nil）：
///     respawn.primaryMtfWave()     -> MtfWave | nil
///     respawn.primaryChaosWave()   -> ChaosWave | nil
///     respawn.miniMtfWave()        -> MiniMtfWave | nil
///     respawn.miniChaosWave()      -> MiniChaosWave | nil
///     respawn.get(waveOrBase)      -> RespawnWave | nil
///
/// 实例成员（<c>wave.influence</c> / <c>wave.respawnTokens</c> / <c>wave.timeLeft</c> /
/// <c>wave.maxWaveSize</c> / <c>wave.initiateRespawn()</c> / <c>wave.instantRespawn()</c> /
/// <c>wave.playAnnouncement()</c> / <c>wave.tryGetCurrentMilestone()</c> 等）
/// 由 LuaObjectProxy 反射覆盖。
/// </summary>
public class LuaRespawnProxy : IRegisterLua
{
    public string Name { get; } = "respawn";

    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var cache = new LuaObjectCache(script);

        var t = new LuaTypeMapper(script)
            .UseProxy(cache)
            .Prefix(Name)
            .Scan(typeof(RespawnWaves))
            .IgnoreMethods("Get")
            .ScanStaticMethods(typeof(RespawnWaves))
            .Build();

        // 静态属性是「构造期快照」，改成函数形态保证每次取到当前波次对象。
        t["primaryMtfWave"] = LuaTableFactory.Nullable(cache, () => RespawnWaves.PrimaryMtfWave);
        t["primaryChaosWave"] = LuaTableFactory.Nullable(cache, () => RespawnWaves.PrimaryChaosWave);
        t["miniMtfWave"] = LuaTableFactory.Nullable(cache, () => RespawnWaves.MiniMtfWave);
        t["miniChaosWave"] = LuaTableFactory.Nullable(cache, () => RespawnWaves.MiniChaosWave);

        t["get"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1)
                throw new ScriptRuntimeException($"{Name}.get(waveOrBase)");

            var arg = args[0];

            if (arg.Type == DataType.Table
                && LuaObjectProxy.Unwrap(arg.Table) is SpawnableWaveBase baseWave)
            {
                var wave = RespawnWaves.Get(baseWave);
                return wave is null ? DynValue.Nil : cache.Wrap(wave);
            }

            throw new ScriptRuntimeException($"{Name}.get: 需要波次对象或其 base");
        });

        return t;
    }
}
