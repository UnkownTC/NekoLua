using InventorySystem.Items;

using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;
using NekoLua.Api.TypeScanner;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Game;

/// <summary>
/// item 表：
///
///   静态查找：
///     item.list()                    -- 所有物品
///     item.count()                   -- 数量
///     item.get(serial)               -- 按序号查（ushort）
///     item.getByType(typeName)       -- 按类型筛选
///     item.getByCategory(category)   -- 按分类筛选
///     item.equipped()                -- 已装备的
///     item.ownedBy(player)           -- 指定玩家持有的
///
///   枚举常量：
///     item.types.*                   -- ItemType 全部值（小写字符串）
///     item.categories.*              -- ItemCategory 全部值
///     item.addReasons.*              -- ItemAddReason
///     item.tierFlags.*               -- ItemTierFlags 位值（数字）
///
///   实例属性（走通用代理）：
///     i.type / i.category / i.tierFlags / i.serial /
///     i.isEquipped / i.isDestroyed / i.isPrefab /
///     i.canEquip / i.canHolster / i.canDrop /
///     i.weight / i.currentOwner / i.addReason /
///     i.gameObject / i.base
///
///   实例方法：
///     i:dropItem()                   -- 掉落，返回 pickup
///     i:moveTo(player)               -- 转移到玩家
/// </summary>
internal class LuaItemProxy : IRegisterLua
{
    public string Name { get; } = "item";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);
        var cache = new LuaObjectCache(s);

        t["list"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;
            foreach (var it in Item.List)
                arr[i++] = cache.Wrap(it);
            return DynValue.NewTable(arr);
        });

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(Item.List.Count));

        t["get"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("item.get(serial)");

            var serial = (ushort)args[0].Number;
            var it = Item.Get(serial);
            return it is null ? DynValue.Nil : cache.Wrap(it);
        });

        t["getByType"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("item.getByType(typeName)");

            if (!Enum.TryParse<ItemType>(args[0].String, true, out var type))
                throw new ScriptRuntimeException(
                    $"item.getByType: 未知类型 '{args[0].String}'");

            var arr = new Table(s);
            var i = 1;
            foreach (var it in Item.GetAll(type))
                arr[i++] = cache.Wrap(it);
            return DynValue.NewTable(arr);
        });

        t["getByCategory"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("item.getByCategory(category)");

            if (!Enum.TryParse<ItemCategory>(args[0].String, true, out var cat))
                throw new ScriptRuntimeException(
                    $"item.getByCategory: 未知分类 '{args[0].String}'");

            var arr = new Table(s);
            var i = 1;
            foreach (var it in Item.GetAll(cat))
                arr[i++] = cache.Wrap(it);
            return DynValue.NewTable(arr);
        });

        t["equipped"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;
            foreach (var it in Item.List)
            {
                try
                { if (it.IsEquipped) arr[i++] = cache.Wrap(it); }
                catch { }
            }
            return DynValue.NewTable(arr);
        });

        t["ownedBy"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Table)
                throw new ScriptRuntimeException("item.ownedBy(player)");

            var userIdV = args[0].Table.Get("userId");
            if (userIdV.Type != DataType.String)
                throw new ScriptRuntimeException("item.ownedBy: player 缺少 userId");

            var owner = Player.Get(userIdV.String);
            if (owner is null)
                return DynValue.NewTable(new Table(s));

            var arr = new Table(s);
            var i = 1;

            foreach (var it in Item.List)
            {
                try
                {
                    if (it.CurrentOwner == owner)
                        arr[i++] = cache.Wrap(it);
                }
                catch { }
            }

            return DynValue.NewTable(arr);
        });

        t["types"] = EnumTable(s, typeof(ItemType), asString: true);
        t["categories"] = EnumTable(s, typeof(ItemCategory), asString: true);
        t["addReasons"] = EnumTable(s, typeof(ItemAddReason), asString: true);
        t["tierFlags"] = EnumTable(s, typeof(ItemTierFlags), asString: false);

        return t;
    }
    internal static Table EnumTable(Script s, Type type, bool asString)
    {
        var t = new Table(s);
        var isFlags = type.IsDefined(typeof(FlagsAttribute), false);

        foreach (var name in Enum.GetNames(type))
        {
            var value = Enum.Parse(type, name);
            var luaKey = ToCamel(name);

            t[luaKey] = isFlags || !asString ? DynValue.NewNumber(Convert.ToInt64(value)) : DynValue.NewString(name.ToLowerInvariant());
        }

        return t;
    }
    internal static string ToCamel(string name)
    {
        return string.IsNullOrEmpty(name)
            ? name
            : char.IsLower(name[0])
            ? name
            : char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
