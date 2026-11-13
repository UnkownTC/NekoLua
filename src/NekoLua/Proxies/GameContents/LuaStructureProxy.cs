using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using LabGenerator = LabApi.Features.Wrappers.Generator;
using LabStructure = LabApi.Features.Wrappers.Structure;
using LabWindow = LabApi.Features.Wrappers.Window;

namespace NekoLua.Proxies.GameContents;

/// <summary>
/// structure 表（对应 LabAPI <c>Facility/Structures/*</c> 全家）：
///
///   通用：
///     structure.list()                    -> table   全部 Structure
///     structure.count()                   -> number
///     structure.get(obj)                  -> Structure | nil
///
///   分类集合（均实时枚举）：
///     structure.lockers() / standardLockers() / rifleRackLockers() /
///     structure.pedestalLockers() / largeLockers() / experimentalWeaponLockers() /
///     structure.wallCabinets() / lockerChambers() /
///     structure.workstations() / microPedestals() / windows() / generators()
///
/// 说明：<c>Window</c> 的破坏/修复仍走既有的 <c>window</c> 表（保留兼容）；
/// 实例成员（<c>locker.chambers</c> / <c>locker.fillChambers()</c> /
/// <c>window.health</c> 等）由 LuaObjectProxy 反射覆盖。
/// </summary>
public class LuaStructureProxy : IRegisterLua
{
    public string Name { get; } = "structure";

    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var cache = new LuaObjectCache(script);

        var t = new LuaTypeMapper(script)
            .UseProxy(cache)
            .Prefix(Name)
            .Ignore("Dictionary", "List")
            .Scan(typeof(LabStructure))
            .ScanStaticMethods(typeof(LabStructure))
            .Build();

        t["list"] = LuaTableFactory.Collection(script, cache, () => LabStructure.List);
        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LabStructure.List.Count));

        t["lockers"] = LuaTableFactory.Collection(script, cache, () => Locker.List);
        t["standardLockers"] = LuaTableFactory.Collection(script, cache, () => StandardLocker.List);
        t["rifleRackLockers"] = LuaTableFactory.Collection(script, cache, () => RifleRackLocker.List);
        t["pedestalLockers"] = LuaTableFactory.Collection(script, cache, () => PedestalLocker.List);
        t["largeLockers"] = LuaTableFactory.Collection(script, cache, () => LargeLocker.List);
        t["experimentalWeaponLockers"] =
            LuaTableFactory.Collection(script, cache, () => ExperimentalWeaponLocker.List);
        t["wallCabinets"] = LuaTableFactory.Collection(script, cache, () => WallCabinet.List);
        // LockerChamber 只暴露 Dictionary（没有 List 快捷属性）。
        t["lockerChambers"] = LuaTableFactory.Collection(
            script,
            cache,
            () => LockerChamber.Dictionary.Values);

        t["workstations"] = LuaTableFactory.Collection(script, cache, () => Workstation.List);
        t["microPedestals"] = LuaTableFactory.Collection(script, cache, () => MicroPedestal.List);
        t["windows"] = LuaTableFactory.Collection(script, cache, () => LabWindow.List);
        t["generators"] = LuaTableFactory.Collection(script, cache, () => LabGenerator.List);

        return t;
    }
}
