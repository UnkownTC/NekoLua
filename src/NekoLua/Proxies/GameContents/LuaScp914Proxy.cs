
using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using LabScp914 = LabApi.Features.Wrappers.Scp914;

namespace NekoLua.Proxies.GameContents;

/// <summary>
/// scp914 表（对应 LabAPI <c>Scp914</c>，它同时是 <c>Room</c> 的子类）：
///
///   状态：
///     scp914.instance()                          -> Scp914 | nil
///     scp914.knobSetting                         string    读写（受门控）
///     scp914.mode                                string    读写（受门控）
///     scp914.isUpgrading                         bool      只读
///     scp914.chamberSize                         vec3      只读
///     scp914.knobChangeSequenceTime / upgradeSequenceTime / sequenceCooldown /
///     scp914.doorCloseDelay / itemUpgradeDelay / doorOpenDelay       number 只读
///
///   操作（受 <c>Config.DeniedGlobalApis</c> 的 <c>scp914</c> 标签门控）：
///     scp914.interact(code[, player])
///     scp914.playSound(sound)
///
///   处理器（只读）：
///     scp914.getItemProcessor(itemType)          -> processor | nil
///     scp914.getAllItemProcessors()              -> table    itemType -> processor
///
/// 说明：
///   1. <c>knobSetting</c> 可写会直接改变全服 914 档位，因此写操作挂 <c>scp914</c> 门禁。
///   2. <c>SetItemProcessor&lt;T&gt;</c> 系列是泛型方法，Lua 无法提供类型实参，
///      **不暴露**；如需自定义处理器请在 C# 插件内注册。
///   3. 实例成员（<c>scp914.gate</c> / <c>scp914.intakeDoor</c> / <c>scp914.doors</c> 等）
///      由 LuaObjectProxy 反射覆盖。
/// </summary>
public class LuaScp914Proxy : IRegisterLua
{
    public string Name { get; } = "scp914";

    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var cache = new LuaObjectCache(script);

        var t = new LuaTypeMapper(script)
            .UseProxy(cache)
            .Prefix(Name)
            .Ignore("Dictionary", "List", "ItemProcessorCache", "Instance")
            .IgnoreMethods("SetItemProcessor")
            .Scan(typeof(LabScp914))
            .ScanStaticMethods(typeof(LabScp914))
            .GuardWrites("scp914", "knobSetting", "mode")
            .GuardCalls("scp914", "interact", "playSound")
            .Build();

        // 静态属性 Instance 改成函数形态，保证取到当前实例。
        t["instance"] = LuaTableFactory.Nullable(cache, () => LabScp914.Instance);

        // ItemProcessorCache 的键是 ItemBase（引用类型），对 Lua 无稳定等价物，
        // 用 itemType 为键的只读视图替代。
        t["itemProcessorCacheSize"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LabScp914.ItemProcessorCache.Count));

        return t;
    }
}
