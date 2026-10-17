using MapGeneration;
using MoonSharp.Interpreter;
using NekoLua.Api;
using NekoLua.Api.TypeScanner;
using NekoLua.Loader;
using NekoLua.Registers;
using LabRoom = LabApi.Features.Wrappers.Room;

namespace NekoLua.Proxies.Game;

/// <summary>
/// room 表：
///
///   静态：
///     room.list()                       -- 全部房间
///     room.count()                      -- 数量
///     room.get(name)                    -- 按 RoomName 名筛选，返回数组
///     room.getByShape(shape)            -- 按 RoomShape 筛选
///     room.getByZone(zone)              -- 按 FacilityZone 筛选
///     room.at(position)                 -- 位置所在房间 | nil
///     room.findPath(from, to)           -- 最短路径（数组，不含起点）
///     room.findPath(from, to, weightFn) -- 带权重
///
///   实例（走通用代理，通过 LuaObjectProxy 自动暴露）：
///     name / shape / zone / position / rotation / transform / gameObject
///     isDestroyed / connectedRooms / adjacentRooms / doors /
///     lightController / allLightControllers / players / cameras
///     :getClosestLightController(player)
/// </summary>
internal class LuaRoomProxy : IRegisterLua
{
    public string Name { get; } = "room";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);
        var cache = new LuaObjectCache(s);


        t["list"] = DynValue.NewCallback((_, _) =>
            WrapAll(s, cache, LabRoom.List));

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LabRoom.List.Count));


        t["get"] = DynValue.NewCallback((_, args) =>
        {
            return args.Count < 1 || args[0].Type != DataType.String
                ? throw new ScriptRuntimeException("room.get(name)")
                : !Enum.TryParse<RoomName>(args[0].String, true, out var name)
                ? throw new ScriptRuntimeException(
                    $"room.get: 未知房间名 '{args[0].String}'")
                : WrapAll(s, cache, LabRoom.Get(name));
        });

        t["getByShape"] = DynValue.NewCallback((_, args) =>
        {
            return args.Count < 1 || args[0].Type != DataType.String
                ? throw new ScriptRuntimeException("room.getByShape(shape)")
                : !Enum.TryParse<RoomShape>(args[0].String, true, out var shape)
                ? throw new ScriptRuntimeException(
                    $"room.getByShape: 未知形状 '{args[0].String}'")
                : WrapAll(s, cache, LabRoom.Get(shape));
        });

        t["getByZone"] = DynValue.NewCallback((_, args) =>
        {
            return args.Count < 1 || args[0].Type != DataType.String
                ? throw new ScriptRuntimeException("room.getByZone(zone)")
                : !Enum.TryParse<FacilityZone>(args[0].String, true, out var zone)
                ? throw new ScriptRuntimeException(
                    $"room.getByZone: 未知区域 '{args[0].String}'")
                : WrapAll(s, cache, LabRoom.Get(zone));
        });

        t["at"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Table)
                throw new ScriptRuntimeException("room.at(position)");

            var pos = LuaVector.UnpackVector3Args(args[0], "room.at");
            var r = LabRoom.GetRoomAtPosition(pos);

            return r is null ? DynValue.Nil : cache.Wrap(r);
        });


        t["findPath"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2)
                throw new ScriptRuntimeException("room.findPath(from, to[, weightFn])");

            var from = UnwrapRoom(args[0], "room.findPath");
            var to = UnwrapRoom(args[1], "room.findPath");

            List<LabRoom> path;

            if (args.Count >= 3 && args[2].Type == DataType.Function)
            {
                var weightFn = args[2];
                path = LabRoom.FindPath(from, to, r =>
                {
                    var v = weightFn.Function.Call(cache.Wrap(r));
                    return v.Type == DataType.Number ? (int)v.Number : 1;
                });
            }
            else
            {
                path = LabRoom.FindPath(from, to);
            }

            var arr = new Table(s);
            for (var i = 0; i < path.Count; i++)
                arr[i + 1] = cache.Wrap(path[i]);

            return DynValue.NewTable(arr);
        });

        return t;
    }
    private static LabRoom UnwrapRoom(DynValue v, string sig)
    {
        if (v.Type != DataType.Table)
            throw new ScriptRuntimeException($"{sig}: 参数需要 room 代理");

        var obj = LuaObjectProxy.Unwrap(v.Table);
        return obj is LabRoom room ? room : throw new ScriptRuntimeException($"{sig}: 参数不是有效的 Room 代理");
    }
    private static DynValue WrapAll<T>(
        Script s, LuaObjectCache cache, IEnumerable<T> source)
    {
        var arr = new Table(s);
        var i = 1;
        foreach (var item in source)
            arr[i++] = cache.Wrap(item!);
        return DynValue.NewTable(arr);
    }
}