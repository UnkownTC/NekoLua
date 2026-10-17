using CommandSystem;
using NekoLua.Api;
using NekoLua.Loader;

namespace NekoLua.Commands;

[CommandHandler(typeof(NekoLuaParentCommand))]
internal class ListCommand : ICommand
{
    public string Command { get; } = "list";
    public string[] Aliases { get; } = ["l", "ls"];
    public string Description { get; } = "列出所有已加载的 Lua mod";
    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!LuaPermissions.HasPermission(sender))
        {
            response =
                $"权限不足！ 需要拥有权限：\n" +
                $"  - ServerConsoleCommands 或 nekolua.manage（permissions.yml内容）";
            return false;
        }
        var mods = LuaModLoader.Mods;

        if (mods.Count == 0)
        {
            response = "没有已加载的 Lua mod";
            return true;
        }

        var lines = new List<string>
        {
            $"共 {mods.Count} 个 Lua mod："
        };
        var index = 1;
        foreach (var mod in mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
        {
            var status = mod.Disabled ? "[禁用]" : "[正常]";
            lines.Add($"  {index++}. {status} {mod.Name}  ({mod.ModDir})");
        }

        response = string.Join("\n", lines);
        return true;
    }
}
