
using MapGeneration;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using UnityEngine;

using LabPocket = LabApi.Features.Wrappers.PocketDimension;

namespace NekoLua.Proxies.GameContents;

/// <summary>
/// pocket 表（对应 LabAPI <c>Facility/Rooms/PocketDimension/*</c>）：
///
///   集合与实例：
///     pocket.instance()                            -> PocketDimension | nil
///     pocket.teleports()                           -> table   全部传送点
///     pocket.items()                               -> table   全部口袋物品
///     pocket.recycleChances()                      -> table   float 数组
///     pocket.minPocketItemTriggerDelay             number    只读
///     pocket.maxPocketItemTriggerDelay             number    只读
///
///   查询：
///     pocket.isPlayerInside(player)                -> bool
///     pocket.isPickupInside(pickup)                -> bool
///     pocket.getCaughtPosition(player)             -> vec3
///     pocket.getRarity(pickup | item)              -> number
///     pocket.getExitPosesForZone(zone)             -> table    Pose 数组
///
///   操作（受 <c>Config.DeniedGlobalApis</c> 的 <c>pocket</c> 标签门控）：
///     pocket.forceInside(player) / forceExit(player) / forceKill(player)
///     pocket.randomizeExits()
///     pocket.addExitPosesForZone(zone, poses) / removeExitPosesForZone(zone, poses)
///     pocket.removeAllExitPosesForZone(zone)
///
/// 说明：<c>Pose</c> 是 Unity 结构体，Lua 侧以普通表传入传出（由 LuaTypeMapper /
/// LuaObjectProxy 的自动结构体打包处理）。
/// </summary>
public class LuaPocketProxy : IRegisterLua
{
    public string Name { get; } = "pocket";

    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var cache = new LuaObjectCache(script);

        var t = new LuaTypeMapper(script)
            .UseProxy(cache)
            .Prefix(Name)
            .Ignore(
                "Instance",
                "PocketTeleports",
                "PocketItems",
                "RecycleChances",
                "MinPocketItemTriggerDelay",
                "MaxPocketItemTriggerDelay")
            .IgnoreMethods(
                "ForceInside",
                "ForceExit",
                "ForceKill",
                "RandomizeExits",
                "AddExitPosesForZone",
                "AddExitPoseForZone",
                "RemoveExitPosesForZone",
                "RemoveExitPoseForZone",
                "RemoveAllExitPosesForZone")
            .Scan(typeof(LabPocket))
            .ScanStaticMethods(typeof(LabPocket))
            .Build();

        t["instance"] = LuaTableFactory.Nullable(cache, () => LabPocket.Instance);
        t["teleports"] = LuaTableFactory.Collection(script, cache, () => LabPocket.PocketTeleports);
        t["items"] = LuaTableFactory.Collection(script, cache, () => LabPocket.PocketItems);

        t["recycleChances"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(script);
            var src = LabPocket.RecycleChances;

            if (src is null)
                return DynValue.NewTable(arr);

            for (var i = 0; i < src.Length; i++)
                arr[i + 1] = DynValue.NewNumber(src[i]);

            return DynValue.NewTable(arr);
        });

        t["minPocketItemTriggerDelay"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LabPocket.MinPocketItemTriggerDelay));

        t["maxPocketItemTriggerDelay"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LabPocket.MaxPocketItemTriggerDelay));

        // ---- 门控操作 ----

        t["forceInside"] = Guarded("forceInside", args =>
        {
            LabPocket.ForceInside(LuaArgs.ResolvePlayer(args, 0, "pocket.forceInside(player)"));
            return DynValue.Nil;
        });

        t["forceExit"] = Guarded("forceExit", args =>
        {
            LabPocket.ForceExit(LuaArgs.ResolvePlayer(args, 0, "pocket.forceExit(player)"));
            return DynValue.Nil;
        });

        t["forceKill"] = Guarded("forceKill", args =>
        {
            LabPocket.ForceKill(LuaArgs.ResolvePlayer(args, 0, "pocket.forceKill(player)"));
            return DynValue.Nil;
        });

        t["randomizeExits"] = Guarded("randomizeExits", _ =>
        {
            LabPocket.RandomizeExits();
            return DynValue.Nil;
        });

        t["addExitPosesForZone"] = Guarded("addExitPosesForZone", args =>
        {
            if (args.Count < 2)
                throw new ScriptRuntimeException("pocket.addExitPosesForZone(zone, poses)");

            var zone = LuaArgs.ParseEnum<FacilityZone>(args[0], "pocket.addExitPosesForZone");
            LabPocket.AddExitPosesForZone(zone, ReadPoses(args[1]));
            return DynValue.Nil;
        });

        t["removeExitPosesForZone"] = Guarded("removeExitPosesForZone", args =>
        {
            if (args.Count < 2)
                throw new ScriptRuntimeException("pocket.removeExitPosesForZone(zone, poses)");

            var zone = LuaArgs.ParseEnum<FacilityZone>(args[0], "pocket.removeExitPosesForZone");
            LabPocket.RemoveExitPosesForZone(zone, ReadPoses(args[1]));
            return DynValue.Nil;
        });

        t["removeAllExitPosesForZone"] = Guarded("removeAllExitPosesForZone", args =>
        {
            if (args.Count < 1)
                throw new ScriptRuntimeException("pocket.removeAllExitPosesForZone(zone)");

            var zone = LuaArgs.ParseEnum<FacilityZone>(args[0], "pocket.removeAllExitPosesForZone");
            LabPocket.RemoveAllExitPosesForZone(zone);
            return DynValue.Nil;
        });

        return t;
    }

    private DynValue Guarded(string name, Func<CallbackArguments, DynValue> body)
    {
        return DynValue.NewCallback((_, args) =>
        {
            LuaGlobalApiGuard.EnsureAllowed("pocket", $"{Name}.{name}");
            return body(args);
        });
    }

    private static List<Pose> ReadPoses(DynValue value)
    {
        if (value.Type != DataType.Table)
            throw new ScriptRuntimeException("poses 需要数组表");

        var table = value.Table;
        var list = new List<Pose>(table.Length);

        for (var i = 1; i <= table.Length; i++)
        {
            var item = table.Get(i);

            if (item.Type != DataType.Table)
                throw new ScriptRuntimeException($"poses[{i}] 需要 pose 表");

            var pos = LuaVector.UnpackV3Args(
                item.Table.Get("position") is { Type: DataType.Table } p ? p : item,
                $"poses[{i}].position");

            var rot = item.Table.Get("rotation") is { Type: DataType.Table } r
                ? LuaVector.UnpackQArgs(r, $"poses[{i}].rotation")
                : UnityEngine.Quaternion.identity;

            list.Add(new Pose(pos, rot));
        }

        return list;
    }
}
