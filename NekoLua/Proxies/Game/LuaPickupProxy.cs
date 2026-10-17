using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;
using NekoLua.Api;
using NekoLua.Api.TypeScanner;
using NekoLua.Loader;
using NekoLua.Registers;

using UnityEngine;

namespace NekoLua.Proxies.Game;

/// <summary>
/// pickup 表（地上的掉落物）：
///
///   静态查找：
///     pickup.list()                          -- 所有掉落物
///     pickup.count()                         -- 数量
///     pickup.get(serial)                     -- 按序号查
///     pickup.getByType(typeName)             -- 按类型筛选
///     pickup.getByCategory(category)         -- 按分类筛选
///
///   创建：
///     pickup.create(type, position)
///     pickup.create(type, position, rotation)
///     pickup.create(type, position, rotation, scale)
///     pickup.create(type, position, rotation, scale, networkSpawn)
///       → 返回 pickup 代理 | nil
///
///   实例属性（走通用代理）：
///     p.type / p.category / p.tier / p.serial / p.weight / p.isLocked /
///     p.isInUse / p.isSpawned / p.isDestroyed / p.isPrefab /
///     p.position（读写）/ p.rotation（读写）/ p.lastOwner / p.room /
///     p.gameObject / p.transform / p.base
///
///   实例方法：
///     p:spawn()                              -- 网络生成
///     p:destroy()                            -- 销毁
/// </summary>
internal class LuaPickupProxy : IRegisterLua
{
    public string Name { get; } = "pickup";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);
        var cache = new LuaObjectCache(s);

        t["list"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;
            foreach (var p in Pickup.List)
                arr[i++] = cache.Wrap(p);
            return DynValue.NewTable(arr);
        });

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(Pickup.List.Count));

        t["get"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("pickup.get(serial)");

            var p = Pickup.Get((ushort)args[0].Number);
            return p is null ? DynValue.Nil : cache.Wrap(p);
        });

        t["getByType"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("pickup.getByType(typeName)");

            if (!Enum.TryParse<ItemType>(args[0].String, true, out var type))
                throw new ScriptRuntimeException(
                    $"pickup.getByType: 未知类型 '{args[0].String}'");

            var arr = new Table(s);
            var i = 1;
            foreach (var p in Pickup.List)
            {
                try
                { if (p.Type == type) arr[i++] = cache.Wrap(p); }
                catch { }
            }
            return DynValue.NewTable(arr);
        });

        t["getByCategory"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("pickup.getByCategory(category)");

            if (!Enum.TryParse<ItemCategory>(args[0].String, true, out var cat))
                throw new ScriptRuntimeException(
                    $"pickup.getByCategory: 未知分类 '{args[0].String}'");

            var arr = new Table(s);
            var i = 1;
            foreach (var p in Pickup.List)
            {
                try
                { if (p.Category == cat) arr[i++] = cache.Wrap(p); }
                catch { }
            }
            return DynValue.NewTable(arr);
        });


        t["create"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException(
                    "pickup.create(type, position[, rotation[, scale[, networkSpawn]]])");

            if (!Enum.TryParse<ItemType>(args[0].String, true, out var type))
                throw new ScriptRuntimeException(
                    $"pickup.create: 未知类型 '{args[0].String}'");

            var position = LuaVector.UnpackVector3(args, 1, "pickup.create");

            var rotation = args.Count > 2
                ? LuaVector.UnpackQuaternion(args, 2, "pickup.create")
                : Quaternion.identity;

            var scale = args.Count > 3
                ? LuaVector.UnpackVector3(args, 3, "pickup.create")
                : Vector3.one;

            var networkSpawn = args.Count <= 4
                || args[4].Type != DataType.Boolean
                || args[4].Boolean;

            var p = Pickup.Create(type, position, rotation, scale, networkSpawn);
            return p is null ? DynValue.Nil : cache.Wrap(p);
        });

        return t;
    }
}
