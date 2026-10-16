using LabApi.Features.Wrappers;

using MapGeneration;

using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using Camera = LabApi.Features.Wrappers.Camera;

namespace NekoLua.Proxies.Game;

/// <summary>
/// map 表：
///
///   属性：
///     map.seed                    -- 地图种子
///     map.rooms / doors / elevators / teslas / generators /
///     pickups / ragdolls / cameras / roomLights
///     map.defaultEscapeZone       -- Bounds
///     map.escapeZones             -- table 数组
///
///   随机：
///     map.getRandomRoom([zone|zones])
///     map.getRandomDoor([zone|zones[, includeBorders]])
///     map.getRandomElevator() / getRandomTesla() / ...
///     map.getRandomPickup() / getRandomRagdoll([zone|zones])
///     map.getRandomCamera([zone|zones]) / getRandomLight([zone|zones])
///
///   逃脱区：
///     map.addEscapeZone(bounds)
///     map.removeEscapeZone(bounds)
///
///   灯光：
///     map.turnOffLights([duration][, zone|zones])
///     map.turnOnLights([zone|zones])
///     map.setColorOfLights(color[, zone|zones])
///     map.resetColorOfLights([zone|zones])
/// </summary>
public class LuaMapProxy : IRegisterLua
{
    public string Name { get; } = "map";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);
        var cache = new LuaObjectCache(s);

        // ============================================================
        // 属性
        // ============================================================

        t["seed"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(Map.Seed));

        t["rooms"] = Col(s, cache, () => Map.Rooms);
        t["doors"] = Col(s, cache, () => Map.Doors);
        t["elevators"] = Col(s, cache, () => Map.Elevators);
        t["teslas"] = Col(s, cache, () => Map.Teslas);
        t["generators"] = Col(s, cache, () => Map.Generators);
        t["pickups"] = Col(s, cache, () => Map.Pickups);
        t["ragdolls"] = Col(s, cache, () => Map.Ragdolls);
        t["cameras"] = Col(s, cache, () => Map.Cameras);
        t["roomLights"] = Col(s, cache, () => Map.RoomLights);

        t["defaultEscapeZone"] = DynValue.NewCallback((_, _) =>
            DynValue.NewTable(LuaVector.PackBounds(s, Map.DefaultEscapeZone)));

        t["escapeZones"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var zones = Map.EscapeZones;
            for (var i = 0; i < zones.Count; i++)
                arr[i + 1] = DynValue.NewTable(LuaVector.PackBounds(s, zones[i]));
            return DynValue.NewTable(arr);
        });

        // ============================================================
        // 逃脱区
        // ============================================================

        t["addEscapeZone"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1)
                throw new ScriptRuntimeException("map.addEscapeZone(bounds)");

            Map.AddEscapeZone(LuaVector.UnpackBoundsArgs(args[0], "map.addEscapeZone"));
            return DynValue.Nil;
        });

        t["removeEscapeZone"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1)
                throw new ScriptRuntimeException("map.removeEscapeZone(bounds)");

            Map.RemoveEscapeZone(LuaVector.UnpackBoundsArgs(args[0], "map.removeEscapeZone"));
            return DynValue.Nil;
        });

        // ============================================================
        // 随机获取
        // ============================================================

        t["getRandomRoom"] = DynValue.NewCallback((_, args) =>
        {
            var room = args.Count == 0
                ? Map.GetRandomRoom()
                : TryZones(args[0], out var zones)
                    ? Map.GetRandomRoom(zones)
                    : Map.GetRandomRoom(ParseZone(args[0].String));

            return room is null ? DynValue.Nil : cache.Wrap(room);
        });

        t["getRandomDoor"] = DynValue.NewCallback((_, args) =>
        {
            Door? d;

            if (args.Count == 0)
            {
                d = Map.GetRandomDoor();
            }
            else
            {
                var includeBorders = args.Count > 1
                    && args[1].Type == DataType.Boolean
                    && args[1].Boolean;

                d = TryZones(args[0], out var zones)
                    ? Map.GetRandomDoor(zones, includeBorders)
                    : Map.GetRandomDoor(ParseZone(args[0].String), includeBorders);
            }

            return d is null ? DynValue.Nil : cache.Wrap(d);
        });

        t["getRandomElevator"] = WrapNullable(s, cache, Map.GetRandomElevator);
        t["getRandomTesla"] = WrapNullable(s, cache, Map.GetRandomTesla);
        t["getRandomGenerator"] = WrapNullable(s, cache, Map.GetRandomGenerator);
        t["getRandomPickup"] = WrapNullable(s, cache, Map.GetRandomPickup);

        t["getRandomRagdoll"] = DynValue.NewCallback((_, args) =>
        {
            Ragdoll? r;

            if (args.Count == 0)
                r = Map.GetRandomRagdoll();
            else r = TryZones(args[0], out var zones) ? Map.GetRandomRagdoll(zones) : Map.GetRandomRagdoll(ParseZone(args[0].String));

            return r is null ? DynValue.Nil : cache.Wrap(r);
        });

        t["getRandomCamera"] = DynValue.NewCallback((_, args) =>
        {
            Camera? c;

            if (args.Count == 0)
                c = Map.GetRandomCamera();
            else c = TryZones(args[0], out var zones) ? Map.GetRandomCamera(zones) : Map.GetRandomCamera(ParseZone(args[0].String));

            return c is null ? DynValue.Nil : cache.Wrap(c);
        });

        t["getRandomLight"] = DynValue.NewCallback((_, args) =>
        {
            LightsController? l;

            if (args.Count == 0)
                l = Map.GetRandomLight();
            else l = TryZones(args[0], out var zones) ? Map.GetRandomLight(zones) : Map.GetRandomLight(ParseZone(args[0].String));

            return l is null ? DynValue.Nil : cache.Wrap(l);
        });


        t["turnOffLights"] = DynValue.NewCallback((_, args) =>
        {
            float? duration = null;
            DynValue? zoneArg = null;

            for (var i = 0; i < args.Count; i++)
            {
                var a = args[i];

                if (a.Type == DataType.Number && duration is null)
                    duration = (float)a.Number;
                else if ((a.Type == DataType.String || a.Type == DataType.Table) && zoneArg is null)
                    zoneArg = a;
            }

            if (zoneArg is null)
            {
                if (duration is null)
                    Map.TurnOffLights();
                else
                    Map.TurnOffLights(duration.Value);
            }
            else
            {
                var d = duration ?? float.MaxValue;

                if (TryZones(zoneArg, out var zones))
                    Map.TurnOffLights(d, zones);
                else
                    Map.TurnOffLights(d, ParseZone(zoneArg.String));
            }

            return DynValue.Nil;
        });

        t["turnOnLights"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count == 0)
                Map.TurnOnLights();
            else if (TryZones(args[0], out var zones))
                Map.TurnOnLights(zones);
            else
                Map.TurnOnLights(ParseZone(args[0].String));

            return DynValue.Nil;
        });

        t["setColorOfLights"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1)
                throw new ScriptRuntimeException("map.setColorOfLights(color[, zone|zones])");

            var color = LuaVector.UnpackColorArgs(args[0], "map.setColorOfLights");

            if (args.Count < 2)
                Map.SetColorOfLights(color);
            else if (TryZones(args[1], out var zones))
                Map.SetColorOfLights(color, zones);
            else
                Map.SetColorOfLights(color, ParseZone(args[1].String));

            return DynValue.Nil;
        });

        t["resetColorOfLights"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count == 0)
                Map.ResetColorOfLights();
            else if (TryZones(args[0], out var zones))
                Map.ResetColorOfLights(zones);
            else
                Map.ResetColorOfLights(ParseZone(args[0].String));

            return DynValue.Nil;
        });

        return t;
    }
    private static DynValue Col<T>(
        Script script,
        LuaObjectCache cache,
        Func<IEnumerable<T>> source)
    {
        return DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(script);
            var i = 1;
            foreach (var item in source())
                arr[i++] = cache.Wrap(item!);
            return DynValue.NewTable(arr);
        });
    }

    private static DynValue WrapNullable<T>(
        Script _,
        LuaObjectCache cache,
        Func<T?> getter) where T : class
    {
        return DynValue.NewCallback((_, _) =>
        {
            var v = getter();
            return v is null ? DynValue.Nil : cache.Wrap(v);
        });
    }

    private static FacilityZone ParseZone(string s)
    {
        return !Enum.TryParse<FacilityZone>(s, true, out var zone)
            ? throw new ScriptRuntimeException(
                $"map: 未知区域 '{s}'，可选: " +
                string.Join(" / ", Enum.GetNames(typeof(FacilityZone))))
            : zone;
    }
    private static bool TryZones(DynValue v, out FacilityZone[] zones)
    {
        zones = [];
        if (v.Type != DataType.Table)
            return false;

        var arr = v.Table;
        var list = new List<FacilityZone>(arr.Length);

        for (var i = 1; i <= arr.Length; i++)
        {
            var item = arr.Get(i);
            if (item.Type != DataType.String)
                return false;
            list.Add(ParseZone(item.String));
        }

        zones = [.. list];
        return true;
    }
}
