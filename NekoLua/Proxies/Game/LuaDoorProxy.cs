using Interactables.Interobjects.DoorUtils;

using LabApi.Features.Enums;
using LabApi.Features.Wrappers;

using MapGeneration;

using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Game;
/// <summary>
/// pickup 表：
///   pickup.list() / count()
///   pickup.get(serial) / getByType(name) / getByCategory(name)
///   pickup.create(type, position[, rotation[, scale[, networkSpawn]]])
///
/// 实例：p.type / category / tier / serial / isLocked（读写）/ isInUse（读写）
///      isSpawned / isDestroyed / isPrefab / position（读写）/ rotation（读写）
/// 方法：p:destroy() / spawn()
/// </summary>
public class LuaDoorProxy : IRegisterLua
{
    public string Name { get; } = "door";
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);
        var cache = new LuaObjectCache(s);

        t["list"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;
            foreach (var d in Door.List)
                arr[i++] = cache.Wrap(d);
            return DynValue.NewTable(arr);
        });

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(Door.List.Count));

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
                catch { /* 未初始化，跳过 */ }
            }

            return DynValue.NewTable(arr);
        });

        t["lockReason"] = EnumTable(s, typeof(DoorLockReason));
        t["permissions"] = EnumTable(s, typeof(DoorPermissionFlags));
        t["names"] = EnumTable(s, typeof(DoorName));

        t["gates"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;
            foreach (var g in LabApi.Features.Wrappers.Gate.List)
                arr[i++] = cache.Wrap(g);
            return DynValue.NewTable(arr);
        });

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
                ? DynValue.NewNumber(Convert.ToInt64(value))
                : DynValue.NewString(name.ToLowerInvariant());
        }

        return t;
    }
}
