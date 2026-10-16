using LabApi.Features.Wrappers;
using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;
using UnityEngine;
using LabStructure = LabApi.Features.Wrappers.Structure;

namespace NekoLua.Proxies.Game;

/// <summary>
/// structure 表 —— 游戏结构（发电机 / 储物柜 / 工作台 / MicroHID 台座 等）：
///
///   静态：
///     structure.list()                    -- 全部结构
///     structure.count()                   -- 数量
///     structure.byType(csharpTypeName)    -- 按 C# 类型名筛选（如 "Generator" / "StandardLocker"）
///     structure.byRoom(room)              -- 指定房间内的结构
///     structure.findNear(position, radius) -- 半径内
///
///   实例（走通用代理，运行时类型自动暴露子类成员）：
///     通用：position（读写）/ rotationY（读写）/ room / isDestroyed /
///          gameObject / transform / base / structurePositionSync /
///          spawn() / destroy()
///
///     Generator：isEngaged / isOpen / isUnlocked / requiredPower / …
///     StandardLocker：chambers / isOpen / …
///     Workstation：isOccupied / …
///     MicroPedestal / PedestalLocker / ExperimentalWeaponLocker / …
///     具体成员由各子类的 public 属性决定。
/// </summary>
public class LuaStructureProxy : IRegisterLua
{
    public string Name { get; } = "structure";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);
        var cache = new LuaObjectCache(s);


        t["list"] = DynValue.NewCallback((_, _) =>
            WrapAll(s, cache, LabStructure.List));

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LabStructure.List.Count));


        t["byType"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("structure.byType(name)");

            var wanted = args[0].String;

            var arr = new Table(s);
            var i = 1;

            foreach (var st in LabStructure.List)
            {
                try
                {
                    var typeName = st.GetType().Name;
                    if (string.Equals(typeName, wanted, StringComparison.OrdinalIgnoreCase))
                        arr[i++] = cache.Wrap(st);
                }
                catch { }
            }

            return DynValue.NewTable(arr);
        });

        t["byRoom"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Table)
                throw new ScriptRuntimeException("structure.byRoom(room)");

            var obj = LuaObjectProxy.Unwrap(args[0].Table);
            if (obj is not Room room)
                throw new ScriptRuntimeException(
                    "structure.byRoom: 参数不是有效的 Room 代理");

            var arr = new Table(s);
            var i = 1;

            foreach (var st in LabStructure.List)
            {
                try { if (st.Room == room) arr[i++] = cache.Wrap(st); }
                catch { }
            }

            return DynValue.NewTable(arr);
        });

        t["findNear"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[1].Type != DataType.Number)
                throw new ScriptRuntimeException("structure.findNear(position, radius)");

            var pos = LuaVector.UnpackV3Args(args[0], "structure.findNear");
            var radius = (float)args[1].Number;

            var arr = new Table(s);
            var i = 1;

            foreach (var st in LabStructure.List)
            {
                try
                {
                    if (Vector3.Distance(st.Position, pos) <= radius)
                        arr[i++] = cache.Wrap(st);
                }
                catch { }
            }
            return DynValue.NewTable(arr);
        });
        t["generators"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;
            foreach (var g in Generator.List)
                arr[i++] = cache.Wrap(g);
            return DynValue.NewTable(arr);
        });
        return t;
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