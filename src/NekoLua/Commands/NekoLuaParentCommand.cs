using CommandSystem;

using NekoLua.Dependencies.Extensions;

namespace NekoLua.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
[CommandHandler(typeof(GameConsoleCommandHandler))]
internal class NekoLuaParentCommand : ParentCommand
{
    public override string Command { get; } = "nekolua";
    public override string[] Aliases { get; } = [];
    public override string Description { get; } = "NekoLua 脚本引擎主命令";
    public override void LoadGeneratedCommands()
    {
        RegisterCommand(new ReloadCommand());
        RegisterCommand(new ListCommand());
        RegisterCommand(new RefreshCommand());
    }
    protected override bool ExecuteParent(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        return CommandExtensions.ListSubCommands(this, arguments, out response);
    }
}
