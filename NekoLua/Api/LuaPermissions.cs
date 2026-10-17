using CommandSystem;
using LabApi.Features.Permissions;

namespace NekoLua.Api;

internal static class LuaPermissions
{
    public static bool HasPermission(ICommandSender sender)
    {
        return sender.CheckPermission(PlayerPermissions.ServerConsoleCommands) || sender.HasPermission("nekolua.manage");
    }
}
