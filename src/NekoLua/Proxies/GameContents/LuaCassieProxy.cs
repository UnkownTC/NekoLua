using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using GameCassie = Cassie;
using LabAnnouncer = LabApi.Features.Wrappers.Announcer;

namespace NekoLua.Proxies.GameContents;

/// <summary>
/// cassie 表（对应 LabAPI <c>Announcer</c>）：
///
///   只读属性（反射自 Announcer）：
///     cassie.isSpeaking                bool
///     cassie.lineDatabase              object | nil
///     cassie.allLines                  table   CassieLine 代理对象数组
///     cassie.collectionNames           table   台词集合名
///
///   查询（无副作用）：
///     cassie.isValid(word)                    -> bool
///     cassie.calculateDuration(msg[, speed])  -> number
///     cassie.convertTeam(team, unitName)      -> string
///     cassie.convertNumber(n)                 -> string
///     cassie.convertScp(role)                 -> withoutSpace, withSpace
///
///   播报（受 <c>Config.DeniedGlobalApis</c> 的 <c>cassie</c> 标签门控）：
///     cassie.message(text [, subtitles [, playBackground [, priority [, glitchScale]]]])
///     cassie.glitchyMessage(text, glitchChance, jamChance)
///     cassie.clear()
///
/// 说明：
///   1. LabAPI 的新门面 <c>LabApi.Features.Wrappers.Cassie</c> 整体标记为
///      <c>[Obsolete("Use Announcer instead of Cassie.", error: true)]</c>，
///      因此本表以 **Announcer** 为权威实现，表名沿用 NekoLua 既有的 <c>cassie</c>。
///   2. <c>Announcer.CalculateDuration(string, bool, float)</c> 与旧版
///      <c>Message(string, bool, bool, bool, string)</c> / <c>Message(CassieTtsPayload, float, float)</c>
///      均被 LabAPI 标记为过时，静态绑定器自动跳过；
///      <c>calculateDuration</c> 由宿主代为构造 <c>CassiePlaybackModifiers</c> 后转发。
///   3. <c>ScpTermination(Player, DamageHandlerBase)</c> 需要 Lua 无法构造的伤害处理器对象，
///      不暴露。
/// </summary>
public class LuaCassieProxy : IRegisterLua
{
    public string Name { get; } = "cassie";

    /// <summary>交给手写实现（需门控 / 需构造游戏侧结构体 / 无法映射到 Lua）。</summary>
    private static readonly string[] Handwritten =
    [
        "Message",
        "CalculateDuration",
        "GlitchyMessage",
        "Clear",
        "ScpTermination",
    ];

    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;

        var t = new LuaTypeMapper(script)
            .UseProxy()
            .Prefix(Name)
            .Scan(typeof(LabAnnouncer))
            .ScanStaticFields(typeof(LabAnnouncer))
            .IgnoreMethods(Handwritten)
            .ScanStaticMethods(typeof(LabAnnouncer))
            .Build();

        // ---- 播报（门控） ----

        t["message"] = DynValue.NewCallback((_, args) =>
        {
            LuaGlobalApiGuard.EnsureAllowed("cassie", "cassie.message");

            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException(
                    "cassie.message(text [, subtitles [, playBackground [, priority [, glitchScale]]]])");

            LabAnnouncer.Message(
                args[0].String,
                OptString(args, 1) ?? "",
                OptBool(args, 2) ?? true,
                (float)(OptNumber(args, 3) ?? 0),
                (float)(OptNumber(args, 4) ?? 1));

            return DynValue.Nil;
        });

        t["glitchyMessage"] = DynValue.NewCallback((_, args) =>
        {
            LuaGlobalApiGuard.EnsureAllowed("cassie", "cassie.glitchyMessage");

            if (args.Count < 3
                || args[0].Type != DataType.String
                || args[1].Type != DataType.Number
                || args[2].Type != DataType.Number)
                throw new ScriptRuntimeException(
                    "cassie.glitchyMessage(text, glitchChance, jamChance)");

            LabAnnouncer.GlitchyMessage(
                args[0].String,
                (float)args[1].Number,
                (float)args[2].Number);

            return DynValue.Nil;
        });

        t["clear"] = DynValue.NewCallback((_, _) =>
        {
            LuaGlobalApiGuard.EnsureAllowed("cassie", "cassie.clear");
            LabAnnouncer.Clear();
            return DynValue.Nil;
        });

        // ---- 时长计算 ----

        t["calculateDuration"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("cassie.calculateDuration(msg[, speed])");

            var modifiers = new GameCassie.CassiePlaybackModifiers
            {
                Pitch = (float)(OptNumber(args, 1) ?? 1),
            };

            return DynValue.NewNumber(LabAnnouncer.CalculateDuration(args[0].String, modifiers));
        });

        return t;
    }

    private static string? OptString(CallbackArguments a, int i)
        => a.Count > i && a[i].Type == DataType.String ? a[i].String : null;

    private static bool? OptBool(CallbackArguments a, int i)
        => a.Count > i && a[i].Type == DataType.Boolean ? a[i].Boolean : null;

    private static double? OptNumber(CallbackArguments a, int i)
        => a.Count > i && a[i].Type == DataType.Number ? a[i].Number : null;
}
