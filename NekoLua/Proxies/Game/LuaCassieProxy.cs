using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using LabAnnouncer = LabApi.Features.Wrappers.Announcer;

namespace NekoLua.Proxies.Game;

/// <summary>
/// cassie 表：
///
///   属性（反射自 Announcer）：
///     cassie.isSpeaking                bool     是否正在播报
///
///   方法：
///     cassie.message(text [, subtitles [, playBackground [, priority [, glitchScale]]]])
///       text            播报文本
///       subtitles       字幕（默认 ""）
///       playBackground  是否播放背景音效（默认 true）
///       priority        优先级（默认 0）
///       glitchScale     失真度（默认 1）
///
///     cassie.glitchyMessage(text, glitchChance, jamChance)
///       带干扰的播报
///
///     cassie.clear()                   打断当前 + 清空队列
///     cassie.isValid(word) -> bool     词库检查
/// </summary>
public class LuaCassieProxy : IRegisterLua
{
    public string Name { get; } = "cassie";
    public Table Create(LuaModInstance mod)
    {
        var t = new LuaTypeMapper(mod.Script)
            .Scan(typeof(LabAnnouncer))
            .Build();

        t["message"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException(
                    $"{Name}.message(text [, subtitles [, playBackground [, priority [, glitchScale]]]])");

            var text = args[0].String;
            var subtitles = OptString(args, 1) ?? "";
            var background = OptBool(args, 2) ?? true;
            var priority = (float)(OptNumber(args, 3) ?? 0);
            var glitch = (float)(OptNumber(args, 4) ?? 1);

            LabAnnouncer.Message(text, subtitles, background, priority, glitch);
            return DynValue.Nil;
        });

        t["glitchyMessage"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 3 ||
                args[0].Type != DataType.String ||
                args[1].Type != DataType.Number ||
                args[2].Type != DataType.Number)
                throw new ScriptRuntimeException(
                    $"{Name}.glitchyMessage(text, glitchChance, jamChance)");

            LabAnnouncer.GlitchyMessage(
                args[0].String,
                (float)args[1].Number,
                (float)args[2].Number);

            return DynValue.Nil;
        });

        t["clear"] = DynValue.NewCallback((_, _) =>
        {
            LabAnnouncer.Clear();
            return DynValue.Nil;
        });

        t["isValid"] = DynValue.NewCallback((_, args) =>
        {
            return args.Count < 1 || args[0].Type != DataType.String
                ? throw new ScriptRuntimeException($"{Name}.isValid(word)")
                : DynValue.NewBoolean(LabAnnouncer.IsValid(args[0].String));
        });

        return t;
    }
    static string? OptString(CallbackArguments a, int i)
    {
        return a.Count > i && a[i].Type == DataType.String ? a[i].String : null;
    }

    static bool? OptBool(CallbackArguments a, int i)
    {
        return a.Count > i && a[i].Type == DataType.Boolean ? a[i].Boolean : null;
    }

    static double? OptNumber(CallbackArguments a, int i)
    {
        return a.Count > i && a[i].Type == DataType.Number ? a[i].Number : null;
    }
}
