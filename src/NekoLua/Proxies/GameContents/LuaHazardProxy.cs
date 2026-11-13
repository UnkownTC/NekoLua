using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using UnityEngine;

using LabHazard = LabApi.Features.Wrappers.Hazard;
using LabTesla = LabApi.Features.Wrappers.Tesla;

namespace NekoLua.Proxies.GameContents;

/// <summary>
/// hazard 表（对应 LabAPI <c>Facility/Hazards/*</c>）：
///
///   集合：
///     hazard.list()             -> table    全部环境危害
///     hazard.count()            -> number
///     hazard.teslas()           -> table    Tesla 电网
///     hazard.tantrums()         -> table    恶心圈
///     hazard.sinkholes()        -> table    天坑
///     hazard.amnesticClouds()   -> table    记忆消除云
///
///   生成（受 <c>Config.DeniedGlobalApis</c> 的 <c>hazard</c> 标签门控）：
///     hazard.spawnTantrum(pos[, rot[, scale]])                          -> TantrumHazard
///     hazard.spawnSinkhole(pos[, rot[, scale]])                         -> SinkholeHazard
///     hazard.spawnAmnesticCloud(pos[, rot[, scale[, duration[, size[, owner]]]]])
///                                                                       -> AmnesticCloudHazard
///     hazard.destroyAll()                                               -> number 销毁数量
///
/// 说明：<c>Tesla</c> 与 <c>Hazard</c> 是两个平行体系（Tesla 不代表 EnvironmentalHazard），
/// 因此 <c>list()</c> 不含电网；实例成员（<c>tesla.trigger()</c> / <c>hazard.isInArea(player)</c>
/// / <c>hazard.destroy()</c>）由 LuaObjectProxy 反射覆盖。
/// </summary>
public class LuaHazardProxy : IRegisterLua
{
    public string Name { get; } = "hazard";

    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var cache = new LuaObjectCache(script);

        var t = new LuaTypeMapper(script)
            .UseProxy(cache)
            .Prefix(Name)
            .Ignore("Dictionary", "List")
            .IgnoreMethods("Spawn")
            .Scan(typeof(LabHazard))
            .ScanStaticMethods(typeof(LabHazard))
            .Build();

        t["list"] = LuaTableFactory.Collection(script, cache, () => LabHazard.List);
        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LabHazard.List.Count));

        t["teslas"] = LuaTableFactory.Collection(script, cache, () => LabTesla.List);
        t["tantrums"] = LuaTableFactory.Collection(script, cache, () => TantrumHazard.List);
        t["sinkholes"] = LuaTableFactory.Collection(script, cache, () => SinkholeHazard.List);
        t["amnesticClouds"] = LuaTableFactory.Collection(
            script,
            cache,
            () => AmnesticCloudHazard.List);

        t["spawnTantrum"] = DynValue.NewCallback((_, args) =>
        {
            LuaGlobalApiGuard.EnsureAllowed("hazard", "hazard.spawnTantrum");

            var p = RequirePosition(args, "hazard.spawnTantrum(pos[, rot[, scale]])");
            var r = OptionalRotation(args, 1);
            var s = OptionalScale(args, 2);

            return cache.Wrap(TantrumHazard.Spawn(p, r, s));
        });

        t["spawnSinkhole"] = DynValue.NewCallback((_, args) =>
        {
            LuaGlobalApiGuard.EnsureAllowed("hazard", "hazard.spawnSinkhole");

            var p = RequirePosition(args, "hazard.spawnSinkhole(pos[, rot[, scale]])");
            var r = OptionalRotation(args, 1);
            var s = OptionalScale(args, 2);

            return cache.Wrap(SinkholeHazard.Spawn(p, r, s));
        });

        t["spawnAmnesticCloud"] = DynValue.NewCallback((_, args) =>
        {
            LuaGlobalApiGuard.EnsureAllowed("hazard", "hazard.spawnAmnesticCloud");

            var p = RequirePosition(
                args,
                "hazard.spawnAmnesticCloud(pos[, rot[, scale[, duration[, size[, owner]]]]])");

            var duration = args.Count > 3 && args[3].Type == DataType.Number
                ? (float)args[3].Number
                : 90f;

            var size = args.Count > 4 && args[4].Type == DataType.Number
                ? LuaNumbers.ClampToByte(args[4].Number, "hazard.spawnAmnesticCloud: size")
                : (byte)255;

            var owner = args.Count > 5
                ? LuaArgs.TryPlayer(args, 5, "hazard.spawnAmnesticCloud: owner")
                : null;

            return cache.Wrap(AmnesticCloudHazard.Spawn(
                p,
                OptionalRotation(args, 1),
                OptionalScale(args, 2),
                duration,
                size,
                owner));
        });

        t["destroyAll"] = DynValue.NewCallback((_, _) =>
        {
            LuaGlobalApiGuard.EnsureAllowed("hazard", "hazard.destroyAll");

            var n = 0;

            foreach (var h in LabHazard.List.ToArray())
            {
                if (h.IsDestroyed)
                    continue;

                h.Destroy();
                n++;
            }

            return DynValue.NewNumber(n);
        });

        return t;
    }

    private static Vector3 RequirePosition(CallbackArguments args, string sig)
    {
        if (args.Count < 1)
            throw new ScriptRuntimeException(sig);

        return LuaVector.UnpackV3Args(args[0], sig);
    }

    private static Quaternion OptionalRotation(CallbackArguments args, int index)
        => args.Count > index && args[index].Type == DataType.Table
            ? LuaVector.UnpackQArgs(args[index], "rot")
            : Quaternion.identity;

    private static Vector3 OptionalScale(CallbackArguments args, int index)
        => args.Count > index && args[index].Type == DataType.Table
            ? LuaVector.UnpackV3Args(args[index], "scale")
            : Vector3.one;
}
