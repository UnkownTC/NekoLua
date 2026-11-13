using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.GameContents;

/// <summary>
/// reservedSlots 表（对称于 <c>whitelist</c>，对应 LabAPI <c>ReservedSlots</c>）：
///   reservedSlots.filePath              -- 预留位配置路径（只读）
///   reservedSlots.list()                -- 所有预留位用户 ID
///   reservedSlots.count()               -- 数量
///   reservedSlots.hasReservedSlot(id | player)  -- 是否持有预留位
///   reservedSlots.reload()              -- 从文件重载
///   reservedSlots.add(id | player)      -- 添加
///   reservedSlots.remove(id | player)   -- 移除，返回 bool
///
/// 写操作受 <c>Config.DeniedGlobalApis</c> 的 <c>reservedSlots</c> 标签门控。
/// </summary>
public class LuaReservedSlotsProxy : IRegisterLua
{
    public string Name { get; } = "reservedSlots";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["filePath"] = DynValue.NewCallback((_, _) =>
            DynValue.NewString(ReservedSlots.FilePath));

        t["list"] = DynValue.NewCallback((_, _) =>
            DynValue.NewTable(LuaArgs.StringArray(s, ReservedSlots.WhitelistedPlayers)));

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(ReservedSlots.Count));

        t["hasReservedSlot"] = DynValue.NewCallback((_, args) =>
        {
            var id = LuaArgs.ResolveUserId(args, 0, "reservedSlots.hasReservedSlot(id | player)");
            return DynValue.NewBoolean(ReservedSlots.HasReservedSlot(id));
        });

        t["reload"] = DynValue.NewCallback((_, _) =>
        {
            LuaGlobalApiGuard.EnsureAllowed("reservedSlots", "reservedSlots.reload");
            ReservedSlots.Reload();
            return DynValue.Nil;
        });

        t["add"] = DynValue.NewCallback((_, args) =>
        {
            LuaGlobalApiGuard.EnsureAllowed("reservedSlots", "reservedSlots.add");

            var id = LuaArgs.ResolveUserId(args, 0, "reservedSlots.add(id | player)");
            ReservedSlots.Add(id);
            return DynValue.Nil;
        });

        t["remove"] = DynValue.NewCallback((_, args) =>
        {
            LuaGlobalApiGuard.EnsureAllowed("reservedSlots", "reservedSlots.remove");

            var id = LuaArgs.ResolveUserId(args, 0, "reservedSlots.remove(id | player)");
            return DynValue.NewBoolean(ReservedSlots.Remove(id));
        });

        return t;
    }
}
