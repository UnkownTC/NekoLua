using Astra.Audio;
using Astra.Countdown;
using Astra.Effects;
using Astra.Enums;
using Astra.Hints;
using Astra.Roles;
using Astra.Teams;
using Astra.Waves;

using LabApi.Features.Console;
using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using Logger = LabApi.Features.Console.Logger;

namespace NekoLua.AstraBridge;

/// <summary>
/// Lua 侧 <c>astra</c> 表：
///   astra.available / version / api_version / flush()
///   astra.team.define/find/list
///   astra.role.define/give/remove/get/find/list
///   astra.wave.define/spawn/find/list
///   astra.hint.show/clear
///   astra.audio.play/stop
///   astra.countdown.start/cancel
///   astra.effect.apply/remove
/// </summary>
public sealed class AstraRegister : IRegisterLua
{
    public string Name => "astra";

    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var content = AstraBridge.GetMod(mod);
        var table = new Table(script)
        {
            ["available"] = DynValue.NewBoolean(AstraBridge.Available),
            ["version"] = DynValue.NewString(AstraBridge.AstraVersionText),
            ["api_version"] = DynValue.NewNumber(AstraBridge.ApiVersion),
            ["flush"] = DynValue.NewCallback((_, _) =>
            {
                EnsureAvailable("astra.flush()");
                EnsureMainThread("astra.flush()");
                AstraBridge.Flush();
                return DynValue.NewNumber(AstraBridge.RegisteredCount);
            }),
            ["team"] = BuildTeam(script, mod, content),
            ["role"] = BuildRole(script, mod, content),
            ["wave"] = BuildWave(script, mod, content),
            ["hint"] = BuildHint(script),
            ["audio"] = BuildAudio(script),
            ["countdown"] = BuildCountdown(script),
            ["effect"] = BuildEffect(script),
        };

        return table;
    }

    private static Table BuildTeam(Script script, LuaModInstance mod, ModContent content)
    {
        var t = new Table(script);

        t["define"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.team.define(def)";
            EnsureAvailable(api);
            EnsureMainThread(api);
            Guard("astra.team", api);

            var definition = DefinitionParser.ParseTeam(RequireTable(args, 0, api));
            AstraBridge.DefineTeam(mod, definition);
            return DynValue.NewString(definition.Id);
        });

        t["find"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.team.find(id)";
            EnsureAvailable(api);

            var team = CustomTeamApi.Find(RequireString(args, 0, api));
            return team is null ? DynValue.Nil : content.Cache.Wrap(team);
        });

        t["list"] = DynValue.NewCallback((_, _) =>
        {
            EnsureAvailable("astra.team.list()");

            var array = new Table(script);
            var index = 1;

            foreach (var team in CustomTeamApi.Teams.Values)
                array[index++] = content.Cache.Wrap(team);

            return DynValue.NewTable(array);
        });

        return t;
    }

    private static Table BuildRole(Script script, LuaModInstance mod, ModContent content)
    {
        var t = new Table(script);

        t["define"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.role.define(def)";
            EnsureAvailable(api);
            EnsureMainThread(api);
            Guard("astra.role", api);

            var definition = DefinitionParser.ParseRole(RequireTable(args, 0, api));
            AstraBridge.DefineRole(mod, definition);
            return DynValue.NewString(definition.Id);
        });

        t["give"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.role.give(ply, id)";
            EnsureAvailable(api);
            EnsureMainThread(api);
            Guard("astra.role", api);

            var player = LuaArgs.ResolvePlayer(args, 0, api);
            var id = RequireString(args, 1, api);
            return DynValue.NewBoolean(CustomRoleApi.AddRole(player, id) is not null);
        });

        t["remove"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.role.remove(ply)";
            EnsureAvailable(api);
            EnsureMainThread(api);
            Guard("astra.role", api);

            CustomRoleApi.RemoveRole(LuaArgs.ResolvePlayer(args, 0, api));
            return DynValue.True;
        });

        t["get"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.role.get(ply)";
            EnsureAvailable(api);

            var role = CustomRoleApi.GetRole(LuaArgs.ResolvePlayer(args, 0, api));
            return role is null ? DynValue.Nil : content.Cache.Wrap(role);
        });

        t["find"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.role.find(id)";
            EnsureAvailable(api);

            return CustomRoleApi.TryGetRoleTemplate(RequireString(args, 0, api), out var role)
                ? content.Cache.Wrap(role)
                : DynValue.Nil;
        });

        t["list"] = DynValue.NewCallback((_, _) =>
        {
            EnsureAvailable("astra.role.list()");

            var array = new Table(script);
            var index = 1;

            foreach (var role in CustomRoleApi.RoleTemplates)
                array[index++] = content.Cache.Wrap(role);

            return DynValue.NewTable(array);
        });

        return t;
    }

    private static Table BuildWave(Script script, LuaModInstance mod, ModContent content)
    {
        var t = new Table(script);

        t["define"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.wave.define(def)";
            EnsureAvailable(api);
            EnsureMainThread(api);
            Guard("astra.wave", api);

            var definition = DefinitionParser.ParseWave(RequireTable(args, 0, api));
            AstraBridge.DefineWave(mod, definition);
            return DynValue.NewString(definition.Id);
        });

        t["spawn"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.wave.spawn(id, players?)";
            EnsureAvailable(api);
            EnsureMainThread(api);
            Guard("astra.wave", api);

            var id = RequireString(args, 0, api);
            var wave = CustomWaveApi.Find(id)
                ?? throw new ScriptRuntimeException($"{api}: 找不到波次 '{id}'");

            var players = args.Count > 1 ? CollectPlayers(args[1], api) : Player.List;
            var spawned = CustomWaveApi.Spawn(wave, players);

            var result = new Table(script);
            var index = 1;

            foreach (var player in spawned)
                result[index++] = content.Cache.Wrap(player);

            return DynValue.NewTable(result);
        });

        t["find"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.wave.find(id)";
            EnsureAvailable(api);

            var wave = CustomWaveApi.Find(RequireString(args, 0, api));
            return wave is null ? DynValue.Nil : content.Cache.Wrap(wave);
        });

        t["list"] = DynValue.NewCallback((_, _) =>
        {
            EnsureAvailable("astra.wave.list()");

            var array = new Table(script);
            var index = 1;

            foreach (var wave in CustomWaveApi.Waves.Values)
                array[index++] = content.Cache.Wrap(wave);

            return DynValue.NewTable(array);
        });

        return t;
    }

    private static Table BuildHint(Script script)
    {
        var t = new Table(script);

        t["show"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.hint.show(ply, text, position?, duration?, font_size?, id?)";
            EnsureAvailable(api);
            EnsureMainThread(api);
            Guard("astra", api);

            var player = LuaArgs.ResolvePlayer(args, 0, api);
            var text = RequireString(args, 1, api);
            var position = ParseEnum(args, 2, HintPosition.CenterTop);
            var duration = ParseNumber(args, 3, 3f);
            var fontSize = (int)ParseNumber(args, 4, 20f);
            var messageId = args.Count > 5 && args[5].Type == DataType.String ? args[5].String : null;

            HintApi.SendTo(player, text, duration, position, fontSize, messageId!);
            return DynValue.Nil;
        });

        t["clear"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.hint.clear(ply, id)";
            EnsureAvailable(api);
            EnsureMainThread(api);
            Guard("astra", api);

            HintApi.RemoveHint(LuaArgs.ResolvePlayer(args, 0, api), RequireString(args, 1, api));
            return DynValue.Nil;
        });

        return t;
    }

    private static Table BuildAudio(Script script)
    {
        var t = new Table(script);

        t["play"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.audio.play(ply?, file, volume?)";
            EnsureAvailable(api);
            EnsureMainThread(api);
            Guard("astra", api);

            var index = 0;
            Player? player = null;

            if (args.Count > 0 && !args[0].IsNil() && args[0].Type != DataType.String)
            {
                player = LuaArgs.ResolvePlayer(args, 0, api);
                index = 1;
            }

            var file = RequireString(args, index, api);
            var volume = args.Count > index + 1 && args[index + 1].Type == DataType.Number
                ? (float)args[index + 1].Number
                : 1f;

            return DynValue.NewNumber(player is null
                ? AudioApi.PlayAudio(file, volume)
                : AudioApi.PlayAudio(player, file, volume));
        });

        t["stop"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.audio.stop(ply?)";
            EnsureAvailable(api);
            EnsureMainThread(api);
            Guard("astra", api);

            if (args.Count > 0 && !args[0].IsNil())
                AudioApi.Stop(LuaArgs.ResolvePlayer(args, 0, api));
            else
                AudioApi.Stop();

            return DynValue.Nil;
        });

        return t;
    }

    private static Table BuildCountdown(Script script)
    {
        var t = new Table(script);

        t["start"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.countdown.start(delay, callback, tick?, repeat?)";
            EnsureAvailable(api);
            EnsureMainThread(api);
            Guard("astra", api);

            if (args.Count < 2 || args[0].Type != DataType.Number || args[1].Type != DataType.Function)
                throw new ScriptRuntimeException($"{api}: 前两个参数必须是 延迟(秒) 与 回调函数");

            var delay = (float)args[0].Number;
            var action = args[1];
            var tick = args.Count > 2 && args[2].Type == DataType.Function ? args[2] : DynValue.Nil;
            var repeat = args.Count > 3 && args[3].Type == DataType.Function ? args[3] : DynValue.Nil;

            Action onDone = () => InvokeLua(action, api);
            Action? onTick = tick.IsNil() ? null : () => InvokeLua(tick, api);
            Func<bool>? onRepeat = repeat.IsNil()
                ? null
                : () => InvokeLua(repeat, api) is { Type: DataType.Boolean } result && result.Boolean;

            return DynValue.NewNumber(CountdownApi.Start(delay, onDone, onTick, onRepeat));
        });

        t["cancel"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.countdown.cancel(id)";
            EnsureAvailable(api);
            EnsureMainThread(api);
            Guard("astra", api);

            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException(api);

            CountdownApi.Cancel((int)args[0].Number);
            return DynValue.Nil;
        });

        return t;
    }

    private static Table BuildEffect(Script script)
    {
        var t = new Table(script);

        t["apply"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.effect.apply(ply, effects)";
            EnsureAvailable(api);
            EnsureMainThread(api);
            Guard("astra", api);

            CustomEffectApi.Apply(LuaArgs.ResolvePlayer(args, 0, api), ParseEffects(args, 1, api));
            return DynValue.Nil;
        });

        t["remove"] = DynValue.NewCallback((_, args) =>
        {
            const string api = "astra.effect.remove(ply, effects)";
            EnsureAvailable(api);
            EnsureMainThread(api);
            Guard("astra", api);

            CustomEffectApi.Remove(LuaArgs.ResolvePlayer(args, 0, api), ParseEffects(args, 1, api));
            return DynValue.Nil;
        });

        return t;
    }

    private static List<EffectEntry> ParseEffects(CallbackArguments args, int index, string api)
    {
        if (args.Count <= index || args[index].Type != DataType.Table)
            throw new ScriptRuntimeException($"{api}: 需要效果数组");

        List<EffectEntry> entries = [];

        foreach (var item in LuaSpec.Sequence(args[index].Table))
        {
            if (item.Type != DataType.Table)
                throw new ScriptRuntimeException($"{api}: 每一项必须是 {{effect=..., intensity=..., duration=...}}");

            var entry = item.Table;
            var effect = LuaSpec.OptionalEnum(entry, "effect", api, EffectType.None);

            if (effect == EffectType.None)
                throw new ScriptRuntimeException($"{api}: effect 不能为空");

            var rawIntensity = (int)LuaSpec.OptionalNumber(entry, "intensity", api, 1f);
            rawIntensity = rawIntensity < 0 ? 0 : rawIntensity > 255 ? 255 : rawIntensity;

            var duration = LuaSpec.OptionalNumber(entry, "duration", api, 0f);
            var addDuration = LuaSpec.OptionalBool(entry, "add_duration", api, false);

            entries.Add(EffectEntry.Vanilla(effect, (byte)rawIntensity, duration, addDuration));
        }

        return entries;
    }

    private static Table RequireTable(CallbackArguments args, int index, string signature)
    {
        if (args.Count <= index || args[index].Type != DataType.Table)
            throw new ScriptRuntimeException($"{signature}: 需要一个 table");

        return args[index].Table;
    }

    private static string RequireString(CallbackArguments args, int index, string signature)
    {
        if (args.Count <= index || args[index].Type != DataType.String || string.IsNullOrWhiteSpace(args[index].String))
            throw new ScriptRuntimeException($"{signature}: 需要一个非空字符串");

        return args[index].String;
    }

    private static T ParseEnum<T>(CallbackArguments args, int index, T fallback) where T : struct, Enum
    {
        if (args.Count <= index || args[index].IsNil())
            return fallback;

        return args[index].Type == DataType.String && Enum.TryParse(args[index].String, true, out T parsed)
            ? parsed
            : fallback;
    }

    private static float ParseNumber(CallbackArguments args, int index, float fallback) =>
        args.Count > index && args[index].Type == DataType.Number
            ? (float)args[index].Number
            : fallback;

    private static IEnumerable<Player> CollectPlayers(DynValue value, string api)
    {
        if (value.IsNil())
            return Player.List;

        if (value.Type == DataType.Table)
        {
            List<Player> players = [];

            foreach (var item in LuaSpec.Sequence(value.Table))
            {
                var player = ToPlayer(item, api);

                if (player is not null)
                    players.Add(player);
            }

            return players;
        }

        var single = ToPlayer(value, api);
        return single is null ? [] : [single];
    }

    private static Player? ToPlayer(DynValue value, string api)
    {
        if (value.Type == DataType.Table)
            return LuaObjectProxy.Unwrap(value.Table) as Player;

        if (value.Type == DataType.String)
            return Player.Get(value.String);

        if (value.Type == DataType.Number && value.Number >= 0 && value.Number <= uint.MaxValue)
            return Player.Get((uint)value.Number);

        throw new ScriptRuntimeException($"{api}: 玩家项必须是 player 句柄、userId 或 networkId");
    }

    private static DynValue InvokeLua(DynValue callback, string api)
    {
        try
        {
            return callback.Function.Call();
        }
        catch (InterpreterException ex)
        {
            Logger.Error($"astra 回调异常（{api}）: {ex.DecoratedMessage}");
            return DynValue.Nil;
        }
    }

    private static void EnsureAvailable(string api)
    {
        if (!AstraBridge.Available)
            throw new ScriptRuntimeException(
                $"{api}: astra 不可用（需要 AstraFramework >= {AstraBridge.MinimumAstraVersion}，" +
                $"当前 {AstraBridge.AstraVersionText}）");
    }

    private static void EnsureMainThread(string api) => AstraBridge.EnsureMainThread(api);

    private static void Guard(string tag, string api) => LuaGlobalApiGuard.EnsureAllowed(tag, api);
}
