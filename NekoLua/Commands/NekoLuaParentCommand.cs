using CommandSystem;
using NekoLua.Api;
using NekoLua.Api.Extensions;

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
        if (!LuaPermissions.HasPermission(sender))
        {
            response =
                $"权限不足！ 需要拥有权限：\n" +
                $"  - ServerConsoleCommands 或 nekolua.manage（permissions.yml内容）";
            return false;
        }
        return CommandExtensions.ListSubCommands(this, arguments, out response);
    }
}
