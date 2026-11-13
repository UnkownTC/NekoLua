using LabApi.Features.Wrappers;

using MapGeneration;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using Camera = LabApi.Features.Wrappers.Camera;
using LabMap = LabApi.Features.Wrappers.Map;
using LabRoom = LabApi.Features.Wrappers.Room;

namespace NekoLua.Proxies.GameContents;

/// <summary>
/// room 表（对应 LabAPI <c>Room</c> / <c>LightsController</c> / <c>Camera</c>）：
///
///   集合：
///     room.list()          -> table    全部房间（Room 代理对象数组）
///     room.count()         -> number
///     room.lights()        -> table    全部灯光控制器
///     room.cameras()       -> table    全部摄像头
///
///   查找：
///     room.get(identifier)          -- 按 RoomIdentifier（来自 room 代理的 base 字段）
///     room.byName(name)             -- 按 RoomName 枚举（如 "LczClassDSpawn"）
///     room.byShape(shape)           -- 按 RoomShape 枚举
///     room.byZone(zone)             -- 按 FacilityZone 枚举（如 "Hcz"）
///     room.atPosition(vec3)         -> Room | nil
///     room.findPath(from, to)       -> table   房间寻路
///
///   随机：
///     room.random() / randomLight() / randomCamera()
///
/// 说明：实例成员（<c>room.area</c> / <c>room.zone</c> / <c>lights.lightsEnabled</c> /
/// <c>lights.flickerLights(duration)</c> / <c>room.base</c> 等）由 LuaObjectProxy 反射覆盖。
/// </summary>
public class LuaRoomProxy : IRegisterLua
{
    public string Name { get; } = "room";

    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var cache = new LuaObjectCache(script);

        var t = new LuaTypeMapper(script)
            .UseProxy(cache)
            .Prefix(Name)
            .Ignore("Dictionary", "List")
            .Scan(typeof(LabRoom))
            .ScanStaticMethods(typeof(LabRoom))
            .Build();

        // 自动扫描得到的是「属性快照」形态；这里按 NekoLua 既有约定改成函数，
        // 保证每次调用都重新枚举实时集合。
        t["list"] = LuaTableFactory.Collection(script, cache, () => LabRoom.List);
        t["count"] = DynValue.NewCallback((_, _) => DynValue.NewNumber(LabRoom.List.Count));

        t["lights"] = LuaTableFactory.Collection(script, cache, () => LightsController.List);
        t["cameras"] = LuaTableFactory.Collection(script, cache, () => Camera.List);

        t["byName"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1)
                throw new ScriptRuntimeException($"{Name}.byName(name)");

            var name = LuaArgs.ParseEnum<RoomName>(args[0], $"{Name}.byName");
            return DynValue.NewTable(WrapAll(script, cache, LabRoom.Get(name)));
        });

        t["byShape"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1)
                throw new ScriptRuntimeException($"{Name}.byShape(shape)");

            var shape = LuaArgs.ParseEnum<RoomShape>(args[0], $"{Name}.byShape");
            return DynValue.NewTable(WrapAll(script, cache, LabRoom.Get(shape)));
        });

        t["byZone"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1)
                throw new ScriptRuntimeException($"{Name}.byZone(zone)");

            var zone = LuaArgs.ParseEnum<FacilityZone>(args[0], $"{Name}.byZone");
            return DynValue.NewTable(WrapAll(script, cache, LabRoom.Get(zone)));
        });

        t["atPosition"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1)
                throw new ScriptRuntimeException($"{Name}.atPosition(vec3)");

            var pos = LuaVector.UnpackV3Args(args[0], $"{Name}.atPosition");
            var found = LabRoom.GetRoomAtPosition(pos);

            return found is null ? DynValue.Nil : cache.Wrap(found);
        });

        t["random"] = LuaTableFactory.Nullable(cache, () => LabMap.GetRandomRoom());
        t["randomLight"] = LuaTableFactory.Nullable(cache, () => LabMap.GetRandomLight());
        t["randomCamera"] = LuaTableFactory.Nullable(cache, () => LabMap.GetRandomCamera());

        return t;
    }

    private static Table WrapAll(
        Script script,
        LuaObjectCache cache,
        IEnumerable<LabRoom> rooms)
    {
        var arr = new Table(script);
        var i = 1;

        foreach (var r in rooms)
            arr[i++] = cache.Wrap(r);

        return arr;
    }
}
