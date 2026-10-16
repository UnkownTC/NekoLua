using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using LabServer = LabApi.Features.Wrappers.Server;

namespace NekoLua.Proxies.Game;

/// <summary>
/// server 表：
///
///   属性（反射自 Server）：
///     server.ip                       string    只读
///     server.port                     number    只读
///     server.playerCount              number    只读
///     server.maxPlayers               number    读写
///     server.tps                      number    只读
///     server.maxTps                   number    读写
///     server.spawnProtectDuration     number    读写
///     server.friendlyFire             bool      读写
///     server.serverListName           string    读写
///     server.playerListName           string    读写
///     server.playerListNameRefreshRate number   读写
///
///   方法：
///     server.restart([redirectPort])
///     server.shutdown([redirectPort])
///     server.runCommand(cmd) -> string
/// </summary>
public class LuaServerProxy : IRegisterLua
{
    public string Name { get; } = "server";
    public Table Create(LuaModInstance mod)
    {
        var t = new LuaTypeMapper(mod.Script)
            .Scan(typeof(LabServer))
            .Build();

        t["restart"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count > 0 && args[0].Type == DataType.Number)
                LabServer.Restart((ushort)args[0].Number);
            else
                LabServer.Restart();

            return DynValue.Nil;
        });

        t["shutdown"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count > 0 && args[0].Type == DataType.Number)
                LabServer.Shutdown((ushort)args[0].Number);
            else
                LabServer.Shutdown();

            return DynValue.Nil;
        });

        t["runCommand"] = DynValue.NewCallback((_, args) =>
        {
            return args.Count < 1 || args[0].Type != DataType.String
                ? throw new ScriptRuntimeException("server.runCommand(cmd): cmd 需要字符串")
                : DynValue.NewString(LabServer.RunCommand(args[0].String));
        });

        return t;
    }
}
