using LabApi.Features.Console;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using LabServer = LabApi.Features.Wrappers.Server;

namespace NekoLua.Proxies.GameContents;

/// <summary>
/// server 表（对应 LabAPI <c>Server</c>，**全静态面**）：
///
///   只读属性：
///     server.ip / port / playerCount / reservedSlots / tps
///     server.idleModeActive / idleModeAvailable / isTransparentlyModded
///     server.shutdownState / host / defaultPermissionProvider
///
///   可写属性（受 <c>Config.DeniedGlobalApis</c> 的 <c>server</c> 标签门控）：
///     server.maxPlayers / maxTps / spawnProtectDuration / friendlyFire /
///     server.pauseIdleMode / serverListName / playerListName / playerListNameRefreshRate
///
///   查询（无副作用）：
///     server.isPlayerBanned(value) / getAllBannedPlayers([banType])
///
///   管理（受 <c>server</c> 标签门控）：
///     server.banPlayer(player | id, reason, duration[, issuer[, nickname]])
///     server.kickPlayer(player | id, reason[, issuer])
///     server.banUserId(userId, reason, duration[, nickname])
///     server.banIpAddress(ip, reason, duration[, nickname])
///     server.unbanUserId(userId) / unbanIpAddress(ip)
///     server.sendBroadcast(msg, duration[, flags[, clearPrevious]])
///     server.sendBroadcast(player, msg, duration[, flags[, clearPrevious]])
///     server.sendAdminChatMessage(msg[, isSilent])
///     server.sendAdminChatMessage(players, msg[, isSilent])
///     server.clearBroadcasts([player])
///
///   生命周期（受 <c>server</c> 标签门控）：
///     server.restart([redirectPort])
///     server.shutdown([redirectPort])
///
///   控制台命令（额外受 <c>Config.AllowScriptRunCommand</c> +
///   <c>Config.RunCommandPrefixWhitelist</c> 双层门控，并写审计日志）：
///     server.runCommand(cmd) -> string
/// </summary>
public class LuaServerProxy : IRegisterLua
{
    public string Name { get; } = "server";

    /// <summary>需要门控或已手写的方法名（C# 名，用于 <see cref="LuaTypeMapper.IgnoreMethods"/>）。</summary>
    private static readonly string[] Handwritten =
    [
        "Restart",
        "Shutdown",
        "RunCommand",
    ];

    /// <summary>一次调用即改变全服状态的方法，统一挂 <c>server</c> 门禁。</summary>
    private static readonly string[] CallGuarded =
    [
        "banPlayer",
        "kickPlayer",
        "banUserId",
        "banIpAddress",
        "unbanUserId",
        "unbanIpAddress",
        "sendBroadcast",
        "sendAdminChatMessage",
        "clearBroadcasts",
    ];

    /// <summary>会改动服务器对外表现/规则的可写属性。</summary>
    private static readonly string[] WriteGuarded =
    [
        "maxPlayers",
        "maxTps",
        "spawnProtectDuration",
        "friendlyFire",
        "pauseIdleMode",
        "serverListName",
        "playerListName",
        "playerListNameRefreshRate",
        "achievementsEnabled",
    ];

    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var cache = new LuaObjectCache(script);

        var t = new LuaTypeMapper(script)
            .UseProxy(cache)
            .Prefix(Name)
            .Scan(typeof(LabServer))
            .IgnoreMethods(Handwritten)
            .ScanStaticMethods(typeof(LabServer))
            .GuardCalls("server", CallGuarded)
            .GuardWrites("server", WriteGuarded)
            .Build();

        t["restart"] = DynValue.NewCallback((_, args) =>
        {
            LuaGlobalApiGuard.EnsureAllowed("server", "server.restart");

            if (args.Count > 0 && args[0].Type == DataType.Number)
                LabServer.Restart(LuaNumbers.ToUInt16(args[0].Number, "server.restart: delay（秒）"));
            else
                LabServer.Restart();

            return DynValue.Nil;
        });

        t["shutdown"] = DynValue.NewCallback((_, args) =>
        {
            LuaGlobalApiGuard.EnsureAllowed("server", "server.shutdown");

            if (args.Count > 0 && args[0].Type == DataType.Number)
                LabServer.Shutdown(LuaNumbers.ToUInt16(args[0].Number, "server.shutdown: delay（秒）"));
            else
                LabServer.Shutdown();

            return DynValue.Nil;
        });

        t["runCommand"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("server.runCommand(cmd): cmd 需要字符串");

            var cmd = args[0].String;

            // 审查 P1-1：LabServer.RunCommand 以服务器控制台身份（全权限）执行，
            // 等同把管理员权限交给每个 mod。默认关闭 + 前缀白名单 + 审计日志。
            if (!MainPlugin.CacheConfig.AllowScriptRunCommand)
                throw new ScriptRuntimeException(
                    "server.runCommand: 已被服务器禁用（Config.AllowScriptRunCommand = false）");

            var prefixes = MainPlugin.CacheConfig.RunCommandPrefixWhitelist;

            if (prefixes is { Length: > 0 })
            {
                var allowed = false;

                foreach (var prefix in prefixes)
                {
                    if (!string.IsNullOrEmpty(prefix) &&
                        cmd.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        allowed = true;
                        break;
                    }
                }

                if (!allowed)
                    throw new ScriptRuntimeException(
                        "server.runCommand: 该命令不在 Config.RunCommandPrefixWhitelist 允许的前缀内");
            }

            Logger.Info($"[NekoLua][audit] {mod.Name} -> server.runCommand: {cmd}");

            return DynValue.NewString(LabServer.RunCommand(cmd));
        });

        return t;
    }
}
