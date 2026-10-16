using LabApi.Features.Wrappers;
using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Game;

/// <summary>
/// reservedslot 表 —— 预留位管理：
///
///   reservedslot.filePath                      -- 配置文件路径
///   reservedslot.list()                        -- 全部预留位用户 ID
///   reservedslot.count()                       -- 数量
///   reservedslot.has(id | player)              -> bool
///   reservedslot.reload()                      -- 从文件重载
///   reservedslot.add(id | player)
///   reservedslot.remove(id | player)           -> bool
/// </summary>
public class LuaReservedSlotProxy : IRegisterLua
{
    public string Name { get; } = "reservedslot";
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["filePath"] = DynValue.NewCallback((_, _) =>
            DynValue.NewString(ReservedSlots.FilePath));

        t["list"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;
            foreach (var id in ReservedSlots.WhitelistedPlayers)
                arr[i++] = DynValue.NewString(id);
            return DynValue.NewTable(arr);
        });

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(ReservedSlots.Count));

        t["has"] = DynValue.NewCallback((_, args) =>
        {
            var id = ResolveId(args, "reservedslot.has(id | player)");
            return DynValue.NewBoolean(ReservedSlots.HasReservedSlot(id));
        });

        t["reload"] = DynValue.NewCallback((_, _) =>
        {
            ReservedSlots.Reload();
            return DynValue.Nil;
        });

        t["add"] = DynValue.NewCallback((_, args) =>
        {
            var id = ResolveId(args, "reservedslot.add(id | player)");
            ReservedSlots.Add(id);
            return DynValue.Nil;
        });

        t["remove"] = DynValue.NewCallback((_, args) =>
        {
            var id = ResolveId(args, "reservedslot.remove(id | player)");
            return DynValue.NewBoolean(ReservedSlots.Remove(id));
        });

        return t;
    }
    private static string ResolveId(CallbackArguments args, string sig)
    {
        if (args.Count < 1)
            throw new ScriptRuntimeException(sig);

        var a = args[0];

        if (a.Type == DataType.String)
            return a.String;

        if (a.Type == DataType.Table)
        {
            var userId = a.Table.Get("userId");
            if (userId.Type == DataType.String) return userId.String;
        }

        throw new ScriptRuntimeException(sig + "（需要 userId 字符串或 player）");
    }
}