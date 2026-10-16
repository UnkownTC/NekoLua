using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Game;

/// <summary>
/// warhead 表：
///
///   状态（只读）：
///     warhead.exists                    bool
///     warhead.isDetonated               bool
///     warhead.isDetonationInProgress    bool
///     warhead.scenarioType              string
///
///   状态（读写）：
///     warhead.leverStatus               bool
///     warhead.isAuthorized              bool
///     warhead.isLocked                  bool
///     warhead.detonationTime            number   剩余秒数
///     warhead.cooldownTime              number
///     warhead.forceCountdownToggle      bool
///     warhead.deadManSwitchRemaining    number
///     warhead.deadManSwitchMaxTime      number
///
///   方法：
///     warhead.start([isAutomatic[, suppressSubtitles[, activator]]])
///     warhead.stop([activator])
///     warhead.detonate()
///     warhead.openBlastDoors() / closeBlastDoors()
///     warhead.shake()
///
///   场景：
///     warhead.startScenarios      -- table 数组
///     warhead.resumeScenarios
///     warhead.deadmanSwitchScenario
/// </summary>
public class LuaWarheadProxy : IRegisterLua
{
    public string Name { get; } = "warhead";
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);
        var cache = new LuaObjectCache(s);


        t["exists"] = DynValue.NewCallback((_, _) =>
            DynValue.NewBoolean(Warhead.Exists));

        t["isDetonated"] = DynValue.NewCallback((_, _) =>
            DynValue.NewBoolean(Warhead.IsDetonated));

        t["isDetonationInProgress"] = DynValue.NewCallback((_, _) =>
            DynValue.NewBoolean(Warhead.IsDetonationInProgress));

        t["scenarioType"] = DynValue.NewCallback((_, _) =>
            DynValue.NewString(Warhead.ScenarioType.ToString().ToLowerInvariant()));

        t["baseController"] = DynValue.NewCallback((_, _) =>
            Warhead.BaseController is null
                ? DynValue.Nil
                : cache.Wrap(Warhead.BaseController));


        t["leverStatus"] = GetSet(
            () => Warhead.LeverStatus,
            v => Warhead.LeverStatus = v);

        t["isAuthorized"] = GetSet(
            () => Warhead.IsAuthorized,
            v => Warhead.IsAuthorized = v);

        t["isLocked"] = GetSet(
            () => Warhead.IsLocked,
            v => Warhead.IsLocked = v);

        t["detonationTime"] = GetSetN(
            () => Warhead.DetonationTime,
            v => Warhead.DetonationTime = (float)v);

        t["cooldownTime"] = GetSetN(
            () => Warhead.CooldownTime,
            v => Warhead.CooldownTime = v);

        t["forceCountdownToggle"] = GetSet(
            () => Warhead.ForceCountdownToggle,
            v => Warhead.ForceCountdownToggle = v);

        t["deadManSwitchRemaining"] = GetSetN(
            () => Warhead.DeadManSwitchRemaining,
            v => Warhead.DeadManSwitchRemaining = (float)v);

        t["deadManSwitchMaxTime"] = GetSetN(
            () => Warhead.DeadManSwitchMaxTime,
            v => Warhead.DeadManSwitchMaxTime = (float)v);

        t["startScenarios"] = DynValue.NewCallback((_, _) =>
            PackScenarios(s, Warhead.StartScenarios));

        t["resumeScenarios"] = DynValue.NewCallback((_, _) =>
            PackScenarios(s, Warhead.ResumeScenarios));

        t["deadmanSwitchScenario"] = DynValue.NewCallback((_, _) =>
            PackScenario(s, Warhead.DeadmanSwitchScenario));


        t["start"] = DynValue.NewCallback((_, args) =>
        {
            var automatic = args.Count > 0 && args[0].Type == DataType.Boolean && args[0].Boolean;
            var suppress = args.Count > 1 && args[1].Type == DataType.Boolean && args[1].Boolean;
            var activator = args.Count > 2 && args[2].Type == DataType.Table
                ? Player.Get(args[2].Table.Get("userId").String ?? "")
                : null;

            Warhead.Start(automatic, suppress, activator);
            return DynValue.Nil;
        });

        t["stop"] = DynValue.NewCallback((_, args) =>
        {
            var activator = args.Count > 0 && args[0].Type == DataType.Table
                ? Player.Get(args[0].Table.Get("userId").String ?? "")
                : null;

            Warhead.Stop(activator);
            return DynValue.Nil;
        });

        t["detonate"] = DynValue.NewCallback((_, _) =>
        {
            Warhead.Detonate();
            return DynValue.Nil;
        });

        t["openBlastDoors"] = DynValue.NewCallback((_, _) =>
        {
            Warhead.OpenBlastDoors();
            return DynValue.Nil;
        });

        t["closeBlastDoors"] = DynValue.NewCallback((_, _) =>
        {
            Warhead.CloseBlastDoors();
            return DynValue.Nil;
        });

        t["shake"] = DynValue.NewCallback((_, _) =>
        {
            Warhead.Shake();
            return DynValue.Nil;
        });

        return t;
    }
    private static DynValue GetSet(Func<bool> get, Action<bool> set)
    {
        return DynValue.NewCallback((_, args) =>
        {
            if (args.Count == 0)
                return DynValue.NewBoolean(get());

            if (args[0].Type != DataType.Boolean)
                throw new ScriptRuntimeException("warhead: 需要布尔值");

            set(args[0].Boolean);
            return DynValue.Nil;
        });
    }
    private static DynValue GetSetN(Func<double> get, Action<double> set)
    {
        return DynValue.NewCallback((_, args) =>
        {
            if (args.Count == 0)
                return DynValue.NewNumber(get());

            if (args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("warhead: 需要数字");

            set(args[0].Number);
            return DynValue.Nil;
        });
    }
    private static DynValue PackScenarios(
        Script s,
        IReadOnlyList<Warhead.DetonationScenario> scenarios)
    {
        var arr = new Table(s);
        for (var i = 0; i < scenarios.Count; i++)
            arr[i + 1] = PackScenario(s, scenarios[i]);
        return DynValue.NewTable(arr);
    }
    private static DynValue PackScenario(Script s, Warhead.DetonationScenario sc)
    {
        return DynValue.NewTable(new Table(s)
        {
            ["id"] = sc.Id,
            ["type"] = sc.Type.ToString().ToLowerInvariant(),
            ["timeToDetonate"] = sc.TimeToDetonate,
            ["additionalTime"] = sc.AdditionalTime,
            ["totalTime"] = sc.TotalTime,
        });
    }
}
