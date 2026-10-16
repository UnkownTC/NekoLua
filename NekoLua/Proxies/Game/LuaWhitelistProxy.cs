using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Game;

/// <summary>
/// whitelist 表：
///   whitelist.filePath              -- 白名单文件路径（只读）
///   whitelist.list()                -- 所有白名单用户 ID
///   whitelist.count()               -- 数量
///   whitelist.enabled               -- 读写，是否启用
///   whitelist.isOnWhitelist(id)     -- 是否在白名单列表里
///   whitelist.isWhitelisted(id)     -- 是否真正生效（考虑 enabled）
///   whitelist.reload()              -- 从文件重载
///   whitelist.add(id | player)      -- 加白名单
///   whitelist.remove(id | player)   -- 移除，返回 bool
/// </summary>
public class LuaWhitelistProxy : IRegisterLua
{
    public string Name { get; } = "whitelist";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["filePath"] = DynValue.NewCallback((_, _) =>
            DynValue.NewString(Whitelist.FilePath));

        t["list"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;
            foreach (var id in Whitelist.WhitelistedPlayers)
                arr[i++] = DynValue.NewString(id);
            return DynValue.NewTable(arr);
        });

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(Whitelist.Count));

        t["enabled"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count == 0)
                return DynValue.NewBoolean(Whitelist.WhitelistEnabled);

            if (args[0].Type != DataType.Boolean)
                throw new ScriptRuntimeException("whitelist.enabled: 需要布尔值");

            Whitelist.WhitelistEnabled = args[0].Boolean;
            return DynValue.Nil;
        });

        t["isOnWhitelist"] = DynValue.NewCallback((_, args) =>
        {
            var id = ResolveId(args, "whitelist.isOnWhitelist(id | player)");
            return DynValue.NewBoolean(Whitelist.IsOnWhitelist(id));
        });

        t["isWhitelisted"] = DynValue.NewCallback((_, args) =>
        {
            var id = ResolveId(args, "whitelist.isWhitelisted(id | player)");
            return DynValue.NewBoolean(Whitelist.IsWhitelisted(id));
        });

        t["reload"] = DynValue.NewCallback((_, _) =>
        {
            Whitelist.Reload();
            return DynValue.Nil;
        });

        t["add"] = DynValue.NewCallback((_, args) =>
        {
            var id = ResolveId(args, "whitelist.add(id | player)");
            Whitelist.Add(id);
            return DynValue.Nil;
        });

        t["remove"] = DynValue.NewCallback((_, args) =>
        {
            var id = ResolveId(args, "whitelist.remove(id | player)");
            return DynValue.NewBoolean(Whitelist.Remove(id));
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
            if (userId.Type == DataType.String)
                return userId.String;
        }

        throw new ScriptRuntimeException(sig + "（需要 userId 字符串或 player）");
    }
}
