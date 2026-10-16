using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using LabRound = LabApi.Features.Wrappers.Round;

namespace NekoLua.Proxies.Game;

/// <summary>
/// round 表：
///
///   属性（反射自 Round）：
///     round.isRoundStarted / isRoundInProgress / isRoundEnded  bool  只读
///     round.canRoundEnd               bool      只读
///     round.keepRoundOnOne            bool      读写
///     round.isLocked                  bool      读写
///     round.isLobbyLocked             bool      读写
///     round.extraTargets              number    读写
///     round.scpTargetsAmount          number    只读
///     round.totalDeaths               number    只读
///     round.killedBySCPs              number    只读
///     round.escapedClassD             number    只读
///     round.escapedScientists         number    只读
///     round.survivingSCPs             number    只读
///     round.changedIntoZombies        number    只读
///     round.duration                  number    只读（TimeSpan → 秒）
///
///   方法：
///     round.start()
///     round.restart([fast[, override[, action]]])
///       action: "restart" / "shutdown" / "donothing"
///     round.restartSilently()
///     round.end([force]) -> bool
/// </summary>
public class LuaRoundProxy : IRegisterLua
{
    public string Name { get; } = "round";
    public Table Create(LuaModInstance mod)
    {
        var t = new LuaTypeMapper(mod.Script)
            .Scan(typeof(LabRound))
            .Build();

        t["start"] = DynValue.NewCallback((_, _) =>
        {
            LabRound.Start();
            return DynValue.Nil;
        });

        t["restart"] = DynValue.NewCallback((_, args) =>
        {
            var fast = args.Count > 0 && args[0].Type == DataType.Boolean && args[0].Boolean;
            var over = args.Count > 1 && args[1].Type == DataType.Boolean && args[1].Boolean;
            var action = args.Count > 2 && args[2].Type == DataType.String
                ? ParseAction(args[2].String)
                : ServerStatic.NextRoundAction.DoNothing;

            LabRound.Restart(fast, over, action);
            return DynValue.Nil;
        });

        t["restartSilently"] = DynValue.NewCallback((_, _) =>
        {
            LabRound.RestartSilently();
            return DynValue.Nil;
        });

        t["end"] = DynValue.NewCallback((_, args) =>
        {
            var force = args.Count > 0 && args[0].Type == DataType.Boolean && args[0].Boolean;
            return DynValue.NewBoolean(LabRound.End(force));
        });

        return t;
    }
    static ServerStatic.NextRoundAction ParseAction(string s)
    {
        return s.ToLowerInvariant() switch
        {
            "restart" => ServerStatic.NextRoundAction.Restart,
            "shutdown" => ServerStatic.NextRoundAction.Shutdown,
            "donothing" => ServerStatic.NextRoundAction.DoNothing,
            _ => throw new ScriptRuntimeException(
                    $"round.restart: action 必须是 'restart' / 'shutdown' / 'donothing'"),
        };
    }
}
