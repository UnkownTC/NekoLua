using System.Reflection;

using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using UnityEngine;

namespace NekoLua.Proxies.Game;

/// <summary>
/// toy 表 —— AdminToy 系统：
///
///   静态：
///     toy.list() / count() / byType(name) / findNear(pos, r) / destroyAll()
///
///   子类型快捷列表：
///     toy.primitives() / lights() / speakers() /
///     toy.capybaras() / waypoints() / shootingTargets() / interactables()
///
///   创建（返回实例代理）：
///     toy.createSpeaker([position[, rotation[, scale]]])
///     toy.createSpeaker(options)              -- options 模式，见下
///     toy.createPrimitive / createLight / createWaypoint   同上
///
///   createSpeaker 的 options：
///     {
///         position      = vector3,
///         rotation      = quaternion,
///         scale         = vector3,
///         controllerId  = number,      -- 0~255，省略自动分配
///         isSpatial     = bool,        -- 默认 false
///         volume        = number,      -- 默认 1.0
///         minDistance   = number,
///         maxDistance   = number,
///         onlyFor       = player,      -- 只对指定玩家播放（用 ValidPlayers 过滤）
///     }
///
///   实例（走通用代理）：
///     通用：position / rotation / scale / parent / isDestroyed /
///          movementSmoothing / isStatic / syncInterval / spawn() / destroy()
///     SpeakerToy：play(samples[, queue[, loop]]) / pause() / resume() /
///          skip(n) / stop() / isPlaying / isPaused / volume /
///          minDistance / maxDistance / controllerId / isSpatial / isLooping
///     其他子类型：见各自 C# 类定义
/// </summary>
public class LuaToyProxy : IRegisterLua
{
    public string Name { get; } = "toy";
    private static readonly Queue<byte> _freeIds = new();
    static LuaToyProxy()
    {
        for (byte i = 100; i < 200; i++)
            _freeIds.Enqueue(i);
    }
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);
        var cache = new LuaObjectCache(s);


        t["list"] = DynValue.NewCallback((_, _) => WrapAll(s, cache, AdminToy.List));

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(AdminToy.List.Count));

        t["byType"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("toy.byType(name)");

            var name = args[0].String;
            var arr = new Table(s);
            var i = 1;

            foreach (var toy in AdminToy.List)
            {
                try
                {
                    if (string.Equals(toy.GetType().Name, name,
                            StringComparison.OrdinalIgnoreCase))
                        arr[i++] = cache.Wrap(toy);
                }
                catch { }
            }
            return DynValue.NewTable(arr);
        });

        t["findNear"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[1].Type != DataType.Number)
                throw new ScriptRuntimeException("toy.findNear(position, radius)");

            var pos = LuaVector.UnpackV3Args(args[0], "toy.findNear");
            var radius = (float)args[1].Number;

            var arr = new Table(s);
            var i = 1;

            foreach (var toy in AdminToy.List)
            {
                try
                {
                    if (Vector3.Distance(toy.Position, pos) <= radius)
                        arr[i++] = cache.Wrap(toy);
                }
                catch { }
            }
            return DynValue.NewTable(arr);
        });

        t["destroyAll"] = DynValue.NewCallback((_, _) =>
        {
            var n = 0;
            foreach (var toy in AdminToy.List.ToArray())
            {
                try
                { toy.Destroy(); n++; }
                catch { }
            }
            return DynValue.NewNumber(n);
        });

        t["primitives"] = FilterByType<PrimitiveObjectToy>(s, cache);
        t["lights"] = FilterByType<LightSourceToy>(s, cache);
        t["speakers"] = FilterByType<SpeakerToy>(s, cache);
        t["capybaras"] = FilterByType<CapybaraToy>(s, cache);
        t["waypoints"] = FilterByType<WaypointToy>(s, cache);
        t["shootingTargets"] = FilterByType<ShootingTargetToy>(s, cache);
        t["interactables"] = FilterByType<InteractableToy>(s, cache);

        t["createSpeaker"] = CreateSpeaker(s, cache);
        t["createPrimitive"] = CreateSimple<PrimitiveObjectToy>(s, cache);
        t["createLight"] = CreateSimple<LightSourceToy>(s, cache);
        t["createWaypoint"] = CreateSimple<WaypointToy>(s, cache);

        return t;
    }
    private static DynValue CreateSpeaker(Script s, LuaObjectCache cache)
    {
        return DynValue.NewCallback((_, args) =>
        {
            if (args.Count > 0 && args[0].Type == DataType.Table && IsOptionsTable(args[0].Table))
                return CreateFromOptions(s, cache, args[0].Table);

            var position = args.Count > 0 && args[0].Type == DataType.Table
                ? LuaVector.UnpackV3Args(args[0], "toy.createSpeaker")
                : Vector3.zero;

            var rotation = args.Count > 1 && args[1].Type == DataType.Table
                ? LuaVector.UnpackQArgs(args[1], "toy.createSpeaker")
                : Quaternion.identity;

            var scale = args.Count > 2 && args[2].Type == DataType.Table
                ? LuaVector.UnpackV3Args(args[2], "toy.createSpeaker")
                : Vector3.one;

            try
            {
                var spk = SpeakerToy.Create(position, rotation, scale);
                AssignIdIfNeeded(spk);
                return cache.Wrap(spk);
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException(
                    $"toy.createSpeaker: {ex.InnerException?.Message ?? ex.Message}");
            }
        });
    }

    private static bool IsOptionsTable(Table t)
    {
        foreach (var p in t.Pairs)
        {
            if (p.Key.Type != DataType.String)
                continue;
            switch (p.Key.String)
            {
                case "controllerId":
                case "isSpatial":
                case "volume":
                case "minDistance":
                case "maxDistance":
                case "onlyFor":
                    return true;
            }
        }
        return false;
    }
    private static DynValue CreateFromOptions(Script _, LuaObjectCache cache, Table opts)
    {
        var position = opts.Get("position").Type == DataType.Table
            ? LuaVector.UnpackV3Args(opts.Get("position"), "toy.createSpeaker")
            : Vector3.zero;

        var rotation = opts.Get("rotation").Type == DataType.Table
            ? LuaVector.UnpackQArgs(opts.Get("rotation"), "toy.createSpeaker")
            : Quaternion.identity;

        var scale = opts.Get("scale").Type == DataType.Table
            ? LuaVector.UnpackV3Args(opts.Get("scale"), "toy.createSpeaker")
            : Vector3.one;

        try
        {
            var spk = SpeakerToy.Create(position, rotation, scale);

            var cidV = opts.Get("controllerId");
            if (cidV.Type == DataType.Number)
                spk.ControllerId = (byte)cidV.Number;
            else
                AssignIdIfNeeded(spk);

            var spaV = opts.Get("isSpatial");
            if (spaV.Type == DataType.Boolean)
                spk.IsSpatial = spaV.Boolean;

            var volV = opts.Get("volume");
            if (volV.Type == DataType.Number)
                spk.Volume = (float)volV.Number;

            var minV = opts.Get("minDistance");
            if (minV.Type == DataType.Number)
                spk.MinDistance = (float)minV.Number;

            var maxV = opts.Get("maxDistance");
            if (maxV.Type == DataType.Number)
                spk.MaxDistance = (float)maxV.Number;

            var onlyV = opts.Get("onlyFor");
            switch (onlyV.Type)
            {
                case DataType.Table:
                    {
                        var playerObj = LuaObjectProxy.Unwrap(onlyV.Table);
                        if (playerObj is Player target)
                        {
                            // 闭包比较，性能最好
                            spk.ValidPlayers = p => p == target;
                        }
                        else
                        {
                            throw new ScriptRuntimeException(
                                "toy.createSpeaker: onlyFor 不是 Player 代理");
                        }
                        break;
                    }

                case DataType.Function:
                    {
                        var luaFn = onlyV;
                        spk.ValidPlayers = p =>
                        {
                            if (p is null)
                                return false;
                            try
                            {
                                var r = luaFn.Function.Call(
                                    DynValue.NewString(p.UserId ?? ""));
                                return r.Type == DataType.Boolean && r.Boolean;
                            }
                            catch { return false; }
                        };
                        break;
                    }

                case DataType.Nil:
                case DataType.Void:
                    break;

                default:
                    throw new ScriptRuntimeException(
                        "toy.createSpeaker: onlyFor 需要 Player 代理或函数");
            }

            return cache.Wrap(spk);
        }
        catch (ScriptRuntimeException) { throw; }
        catch (Exception ex)
        {
            throw new ScriptRuntimeException(
                $"toy.createSpeaker: {ex.InnerException?.Message ?? ex.Message}");
        }
    }
    private static void AssignIdIfNeeded(SpeakerToy spk)
    {
        if (spk.ControllerId == 0)
        {
            if (_freeIds.Count == 0)
                throw new ScriptRuntimeException(
                    "toy: controllerId 池耗尽（100~199 已满）");
            spk.ControllerId = _freeIds.Dequeue();
        }
    }
    private static DynValue CreateSimple<T>(Script _, LuaObjectCache cache)
        where T : AdminToy
    {
        return DynValue.NewCallback((_, args) =>
        {
            var position = args.Count > 0 && args[0].Type == DataType.Table
                ? LuaVector.UnpackV3Args(args[0], "toy.create")
                : Vector3.zero;

            var rotation = args.Count > 1 && args[1].Type == DataType.Table
                ? LuaVector.UnpackQArgs(args[1], "toy.create")
                : Quaternion.identity;

            var scale = args.Count > 2 && args[2].Type == DataType.Table
                ? LuaVector.UnpackV3Args(args[2], "toy.create")
                : Vector3.one;

            var type = typeof(T);
            var create = type
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m =>
                    m.Name == "Create"
                    && m.GetParameters().Length == 5
                    && m.GetParameters()[0].ParameterType == typeof(Vector3)
                    && m.GetParameters()[1].ParameterType == typeof(Quaternion)
                    && m.GetParameters()[2].ParameterType == typeof(Vector3)) ?? throw new ScriptRuntimeException(
                    $"toy.create: {type.Name} 没有匹配的 Create 方法");
            try
            {
                var result = create.Invoke(null,
                [
                    position, rotation, scale, null, true
                ]);

                return result is AdminToy toy ? cache.Wrap(toy) : DynValue.Nil;
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException(
                    $"toy.create: {ex.InnerException?.Message ?? ex.Message}");
            }
        });
    }
    private static DynValue FilterByType<T>(Script s, LuaObjectCache cache)
        where T : AdminToy
    {
        return DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;

            foreach (var toy in AdminToy.List)
            {
                if (toy is T)
                    arr[i++] = cache.Wrap(toy);
            }
            return DynValue.NewTable(arr);
        });
    }

    private static DynValue WrapAll<T>(Script s, LuaObjectCache cache, IEnumerable<T> source)
    {
        var arr = new Table(s);
        var i = 1;
        foreach (var item in source)
            arr[i++] = cache.Wrap(item!);
        return DynValue.NewTable(arr);
    }
}
