using MoonSharp.Interpreter;
using NekoLua.Dependencies.Enums;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Game;

/// <summary>
/// log 表：
///   log.info(msg)
///   log.warn(msg)
///   log.error(msg)
///   log.debug(msg[, enabled])       -- enabled 默认 true，传 false 静默
/// </summary>
public class LuaLogProxy : IRegisterLua
{
    public string Name { get; } = "log";
    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);
        var prefix = $"[LuaMod-{mod.Name}] ";

        t["info"] = Build(prefix, LuaLogLevel.Info);
        t["warn"] = Build(prefix, LuaLogLevel.Warn);
        t["error"] = Build(prefix, LuaLogLevel.Error);
        t["debug"] = Build(prefix, LuaLogLevel.Debug);
        return t;
    }
    static DynValue Build(string prefix, LuaLogLevel level)
    {
        var tag = $"[{level}] ";
        return DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1)
                return DynValue.Nil;

            if (level == LuaLogLevel.Debug &&
                args.Count >= 2 && !args[1].Boolean)
                return DynValue.Nil;

            string msg = args[0].Type == DataType.String
                ? args[0].String
                : args[0].ToPrintString();

            var line = tag + prefix + msg;
            switch (level)
            {
                case LuaLogLevel.Error:
                    ServerConsole.AddLog(line, ConsoleColor.DarkRed);
                    break;
                case LuaLogLevel.Warn:
                    ServerConsole.AddLog(line, ConsoleColor.DarkYellow);
                    break;
                case LuaLogLevel.Info:
                    ServerConsole.AddLog(line, ConsoleColor.Cyan);
                    break;
                case LuaLogLevel.Debug:
                    ServerConsole.AddLog(line, ConsoleColor.DarkGray);
                    break;
            }
            return DynValue.Nil;
        });
    }
}
