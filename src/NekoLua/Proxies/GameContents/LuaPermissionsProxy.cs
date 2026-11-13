using LabApi.Features.Permissions;
using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.GameContents;

/// <summary>
/// permissions 表（对应 LabAPI <c>PermissionsManager</c> + <c>PermissionsExtensions</c>）：
///
///   查询（不改变服务器状态，不受门控）：
///     permissions.get(player | id)                 -> table   权限字符串数组
///     permissions.byProvider(player | id)          -> table   provider 类型名 -> 权限数组
///     permissions.has(player | id, perm)           -> bool
///     permissions.hasAny(player | id, ...perms)    -> bool
///     permissions.hasAll(player | id, ...perms)    -> bool
///
///   变更（受 <c>Config.DeniedGlobalApis</c> 的 <c>permissions</c> 标签门控）：
///     permissions.add(player | id, ...perms)
///     permissions.remove(player | id, ...perms)
///     permissions.reloadAll()
///
/// 注：权限提供者的注册/反注册需要 C# 泛型 + <c>new()</c> 约束，Lua 无法构造，
/// 因此 <c>registerProvider</c> / <c>unregisterProvider</c> 刻意不暴露。
/// </summary>
public class LuaPermissionsProxy : IRegisterLua
{
    public string Name { get; } = "permissions";

    private const string SigPlayer = "player | id";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["get"] = DynValue.NewCallback((_, args) =>
        {
            var player = Resolve(args, "permissions.get(player | id)");
            return DynValue.NewTable(LuaArgs.StringArray(s, player.GetPermissions()));
        });

        t["byProvider"] = DynValue.NewCallback((_, args) =>
        {
            var player = Resolve(args, "permissions.byProvider(player | id)");
            var result = new Table(s);

            foreach (var kv in player.GetPermissionsByProvider())
                result[kv.Key.Name] = DynValue.NewTable(LuaArgs.StringArray(s, kv.Value));

            return DynValue.NewTable(result);
        });

        t["has"] = DynValue.NewCallback((_, args) =>
        {
            var player = Resolve(args, "permissions.has(" + SigPlayer + ", perm)");
            var perms = LuaArgs.CollectStrings(args, 1, 1, "permissions.has(player | id, perm)");
            return DynValue.NewBoolean(player.HasPermissions(perms));
        });

        t["hasAny"] = DynValue.NewCallback((_, args) =>
        {
            var player = Resolve(args, "permissions.hasAny(" + SigPlayer + ", ...perms)");
            var perms = LuaArgs.CollectStrings(args, 1, 1, "permissions.hasAny(player | id, ...perms)");
            return DynValue.NewBoolean(player.HasAnyPermission(perms));
        });

        t["hasAll"] = DynValue.NewCallback((_, args) =>
        {
            var player = Resolve(args, "permissions.hasAll(" + SigPlayer + ", ...perms)");
            var perms = LuaArgs.CollectStrings(args, 1, 1, "permissions.hasAll(player | id, ...perms)");

            var granted = player.GetPermissions();
            var set = new HashSet<string>(granted, StringComparer.OrdinalIgnoreCase);

            foreach (var p in perms)
            {
                if (!set.Contains(p))
                    return DynValue.False;
            }

            return DynValue.True;
        });

        t["add"] = DynValue.NewCallback((_, args) =>
        {
            LuaGlobalApiGuard.EnsureAllowed("permissions", "permissions.add");

            var player = Resolve(args, "permissions.add(" + SigPlayer + ", ...perms)");
            var perms = LuaArgs.CollectStrings(args, 1, 1, "permissions.add(player | id, ...perms)");

            player.AddPermissions(perms);
            return DynValue.Nil;
        });

        t["remove"] = DynValue.NewCallback((_, args) =>
        {
            LuaGlobalApiGuard.EnsureAllowed("permissions", "permissions.remove");

            var player = Resolve(args, "permissions.remove(" + SigPlayer + ", ...perms)");
            var perms = LuaArgs.CollectStrings(args, 1, 1, "permissions.remove(player | id, ...perms)");

            player.RemovePermissions(perms);
            return DynValue.Nil;
        });

        t["reloadAll"] = DynValue.NewCallback((_, _) =>
        {
            LuaGlobalApiGuard.EnsureAllowed("permissions", "permissions.reloadAll");
            PermissionsManager.ReloadAllPermissionsProviders();
            return DynValue.Nil;
        });

        return t;
    }

    private static Player Resolve(CallbackArguments args, string sig)
        => LuaArgs.ResolvePlayer(args, 0, sig);
}
