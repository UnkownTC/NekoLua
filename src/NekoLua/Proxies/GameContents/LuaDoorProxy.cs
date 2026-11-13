using Interactables.Interobjects.DoorUtils;

using LabApi.Features.Enums;
using LabApi.Features.Wrappers;

using MapGeneration;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.GameContents;
/// <summary>
/// door 表：
///
///   静态查询：
///     door.list()                        -- 所有门
///     door.count()                       -- 数量
///     door.get(id)                       -- 按 DoorName 字符串或门实例 id 取单个门
///     door.byZone(zone)                  -- 按 FacilityZone 枚举名筛选
///     door.byRoom(nameTagPrefix)         -- 按 NameTag 前缀筛选
///
///   子类集合（对应 LabAPI Doors 命名空间下的 8 个 Door 派生包装类）：
///     door.breakableDoors() / bulkheadDoors() / checkpointDoors() / dummyDoors() /
///     door.elevatorDoors() / gates() / nonInteractableDoors() / timed173Gates()
///
///   枚举字典：
///     door.lockReason                    -- DoorLockReason
///     door.permissions                   -- DoorPermissionFlags
///     door.names                         -- DoorName
///
///   实例（走通用代理，成员反射自 Door 包装类）：
///     属性/方法见 LabApi.Features.Wrappers.Door
/// </summary>
public class LuaDoorProxy : IRegisterLua
{
    public string Name { get; } = "door";
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);
        var cache = new LuaObjectCache(s);

        t["list"] = LuaTableFactory.Collection(s, cache, () => Door.List);

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(Door.List.Count));

        t["breakableDoors"] = LuaTableFactory.Collection(s, cache, () => BreakableDoor.List);
        t["bulkheadDoors"] = LuaTableFactory.Collection(s, cache, () => BulkheadDoor.List);
        t["checkpointDoors"] = LuaTableFactory.Collection(s, cache, () => CheckpointDoor.List);
        t["dummyDoors"] = LuaTableFactory.Collection(s, cache, () => DummyDoor.List);
        t["elevatorDoors"] = LuaTableFactory.Collection(s, cache, () => ElevatorDoor.List);
        t["gates"] = LuaTableFactory.Collection(s, cache, () => Gate.List);
        t["nonInteractableDoors"] = LuaTableFactory.Collection(s, cache, () => NonInteractableDoor.List);
        t["timed173Gates"] = LuaTableFactory.Collection(s, cache, () => Timed173Gate.List);

        t["get"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("door.get(id): 需要字符串");

            var id = args[0].String;

            var door = Door.Get(id);
            if (door != null)
                return cache.Wrap(door);

            if (Enum.TryParse<DoorName>(id, ignoreCase: true, out var doorName))
            {
                door = Door.Get(doorName);
                if (door != null)
                    return cache.Wrap(door);
            }

            return DynValue.Nil;
        });

        t["byZone"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("door.byZone(zone)");

            if (!Enum.TryParse<FacilityZone>(args[0].String, true, out var zone))
                throw new ScriptRuntimeException(
                    $"door.byZone: 未知区域 '{args[0].String}'，可选: " +
                    string.Join(" / ", Enum.GetNames(typeof(FacilityZone))));

            var arr = new Table(s);
            var i = 1;
            foreach (var d in Door.Get(zone))
                arr[i++] = cache.Wrap(d);
            return DynValue.NewTable(arr);
        });

        t["byRoom"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("door.byRoom(nameTagPrefix)");

            var prefix = args[0].String;
            var arr = new Table(s);
            var i = 1;

            foreach (var d in Door.List)
            {
                try
                {
                    var tag = d.NameTag;
                    if (!string.IsNullOrEmpty(tag) &&
                        tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        arr[i++] = cache.Wrap(d);
                }
                catch (Exception ex)
                {
                    LuaDiagnostics.WarnOnce("door.byRoom: 读取门名称失败，该门已跳过", ex);
                }
            }

            return DynValue.NewTable(arr);
        });

        t["lockReason"] = EnumTable(s, typeof(DoorLockReason));
        t["permissions"] = EnumTable(s, typeof(DoorPermissionFlags));
        t["names"] = EnumTable(s, typeof(DoorName));

        return t;
    }
    private static Table EnumTable(Script script, Type enumType)
    {
        var t = new Table(script);
        var isFlags = enumType.IsDefined(typeof(FlagsAttribute), false);

        foreach (var name in Enum.GetNames(enumType))
        {
            var value = Enum.Parse(enumType, name);
            var luaKey = char.ToLowerInvariant(name[0]) + name.Substring(1);

            t[luaKey] = isFlags
                ? DynValue.NewNumber(Convert.ToInt64(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture))
                : DynValue.NewString(name.ToLowerInvariant());
        }

        return t;
    }
}
