using Interactables.Interobjects;

using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Game;

/// <summary>
/// elevator 表：
///   elevator.list()                 -- 所有电梯
///   elevator.count()                -- 数量
///   elevator.byGroup(group)         -- 按分组筛选
///   elevator.groups()               -- 所有分组名
///   elevator.lockAll()              -- 锁定所有电梯门
///   elevator.unlockAll()            -- 解锁所有电梯门
///
/// 实例属性：
///   group, isReady, goingUp,
///   currentDestinationLevel, nextDestinationLevel,
///   currentSequence, anyDoorLockedReason, allDoorsLockedReason,
///   dynamicAdminLock, worldSpaceBounds,
///   doors, rooms, currentDestination, nextDestination
///
/// 实例方法：
///   e:setDestination(level[, force])
///   e:sendToNextFloor()
///   e:lockAllDoors() / e:unlockAllDoors()
///   e:interact(player)
///
/// 枚举常量：
///   elevator.lockReason.*           -- DoorLockReason 位值
/// </summary>
public class LuaElevatorProxy : IRegisterLua
{
    public string Name { get; } = "elevator";
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);
        var cache = new LuaObjectCache(s);


        t["list"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;
            foreach (var e in Elevator.List)
                arr[i++] = cache.Wrap(e);
            return DynValue.NewTable(arr);
        });

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(Elevator.List.Count));

        t["byGroup"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("elevator.byGroup(group)");

            if (!Enum.TryParse<ElevatorGroup>(args[0].String, true, out var group))
                throw new ScriptRuntimeException(
                    $"elevator.byGroup: 未知分组 '{args[0].String}'，可选: " +
                    string.Join(" / ", Enum.GetNames(typeof(ElevatorGroup))));

            var arr = new Table(s);
            var i = 1;
            foreach (var e in Elevator.GetByGroup(group))
                arr[i++] = cache.Wrap(e);
            return DynValue.NewTable(arr);
        });

        t["groups"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;
            foreach (var name in Enum.GetNames(typeof(ElevatorGroup)))
                arr[i++] = DynValue.NewString(
                    char.ToLowerInvariant(name[0]) + name.Substring(1));
            return DynValue.NewTable(arr);
        });

        t["lockAll"] = DynValue.NewCallback((_, _) =>
        {
            Elevator.LockAll();
            return DynValue.Nil;
        });

        t["unlockAll"] = DynValue.NewCallback((_, _) =>
        {
            Elevator.UnlockAll();
            return DynValue.Nil;
        });

        var reasonTbl = new Table(s);
        foreach (var name in Enum.GetNames(typeof(Interactables.Interobjects.DoorUtils.DoorLockReason)))
        {
            var value = Enum.Parse(typeof(Interactables.Interobjects.DoorUtils.DoorLockReason), name);
            var luaKey = char.ToLowerInvariant(name[0]) + name.Substring(1);
            reasonTbl[luaKey] = DynValue.NewNumber(Convert.ToInt64(value));
        }
        t["lockReason"] = DynValue.NewTable(reasonTbl);

        return t;
    }
}
