using LabApi.Features.Console;
using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Game;

/// <summary>
/// broadcast 表（玩家屏幕广播）：
///
///   broadcast.send(message, duration[, flags[, clearPrevious]])
///     flags: "normal" / "truncated" / "adminChat"
///            支持 "|" 组合，如 "truncated|adminChat"
///            也可以直接传数字
///     clearPrevious: 默认 false，true 时先清空屏幕
///
///   broadcast.normal / truncated / adminChat(message, duration[, clearPrevious])
///     便捷形式，直接指定 flags
///
///   broadcast.clear()               清空所有玩家屏幕上的广播
///
///   broadcast.flags                 -- 常量表
///     .normal = 0 / .truncated = 1 / .adminChat = 2
/// </summary>
internal class LuaBroadcastProxy : IRegisterLua
{
    public string Name { get; } = "broadcast";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["send"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException(
                    "broadcast.send(message, duration[, flags[, clearPrevious]])");

            var message = args[0].String;
            var duration = ParseDuration(args[1]);
            var flags = args.Count > 2 ? ParseFlags(args[2]) : Broadcast.BroadcastFlags.Normal;
            var clear = args.Count > 3 && args[3].Type == DataType.Boolean && args[3].Boolean;

            return TrySend(message, duration, flags, clear);
        });

        t["normal"] = Shorthand(Broadcast.BroadcastFlags.Normal);
        t["truncated"] = Shorthand(Broadcast.BroadcastFlags.Truncated);
        t["adminChat"] = Shorthand(Broadcast.BroadcastFlags.AdminChat);

        t["clear"] = DynValue.NewCallback((_, _) =>
        {
            if (Broadcast.Singleton is null)
                return DynValue.Nil;

            try
            { Server.ClearBroadcasts(); }
            catch (Exception ex)
            {
                Logger.Warn($"broadcast.clear 失败: {ex.Message}");
            }
            return DynValue.Nil;
        });

        t["flags"] = DynValue.NewTable(new Table(s)
        {
            ["normal"] = (int)Broadcast.BroadcastFlags.Normal,
            ["truncated"] = (int)Broadcast.BroadcastFlags.Truncated,
            ["adminChat"] = (int)Broadcast.BroadcastFlags.AdminChat,
        });

        return t;
    }
    static DynValue TrySend(string message, ushort duration,
                            Broadcast.BroadcastFlags flags, bool clear)
    {
        if (Broadcast.Singleton is null)
        {
            Logger.Warn("broadcast: 游戏尚未初始化，广播未发送");
            return DynValue.Nil;
        }

        try
        {
            Server.SendBroadcast(message, duration, flags, clear);
        }
        catch (Exception ex)
        {
            Logger.Warn($"broadcast.send 失败: {ex.Message}");
        }

        return DynValue.Nil;
    }
    static DynValue Shorthand(Broadcast.BroadcastFlags flags)
    {
        return DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException(
                    "broadcast.<type>(message, duration[, clearPrevious])");

            var message = args[0].String;
            var duration = ParseDuration(args[1]);
            var clear = args.Count > 2 && args[2].Type == DataType.Boolean && args[2].Boolean;

            return TrySend(message, duration, flags, clear);
        });
    }

    static ushort ParseDuration(DynValue v)
    {
        if (v.Type != DataType.Number)
            throw new ScriptRuntimeException("broadcast: duration 需要数字（秒）");

        var d = v.Number;
        return d is < 0 or > ushort.MaxValue
            ? throw new ScriptRuntimeException(
                $"broadcast: duration 超出范围 0~{ushort.MaxValue}")
            : (ushort)d;
    }

    static Broadcast.BroadcastFlags ParseFlags(DynValue v)
    {
        if (v.Type == DataType.Number)
            return (Broadcast.BroadcastFlags)(byte)v.Number;

        if (v.Type == DataType.String)
        {
            var result = (Broadcast.BroadcastFlags)0;

            foreach (var raw in v.String.Split('|'))
            {
                var key = raw.Trim().ToLowerInvariant();
                if (key.Length == 0)
                    continue;

                result |= key switch
                {
                    "normal" => Broadcast.BroadcastFlags.Normal,
                    "truncated" => Broadcast.BroadcastFlags.Truncated,
                    "adminchat" => Broadcast.BroadcastFlags.AdminChat,
                    _ => throw new ScriptRuntimeException(
                            $"broadcast: 未知 flags '{raw}'，可选 normal / truncated / adminChat"),
                };
            }

            return result;
        }

        throw new ScriptRuntimeException("broadcast: flags 需要字符串或数字");
    }
}
