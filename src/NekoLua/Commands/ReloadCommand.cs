using CommandSystem;

using LabApi.Features.Console;

using NekoLua.Loader;

namespace NekoLua.Commands;

[CommandHandler(typeof(NekoLuaParentCommand))]
internal class ReloadCommand : ICommand
{
    public string Command { get; } = "reload";
    public string[] Aliases { get; } = ["r", "rl"];
    public string Description { get; } = "重载 Lua 脚本（无参数=全部，或指定 mod 名）";
    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission(PlayerPermissions.ServerConsoleCommands))
        {
            response =
                $"权限不足！ 需要拥有权限：\n" +
                $"  - ServerConsoleCommands";
            return false;
        }
        var mods = LuaLoader.Mods;

        if (mods.Count == 0)
        {
            response = "没有已加载的 Lua mod";
            return false;
        }

        if (arguments.Count == 0)
        {
            var count = 0;
            foreach (var mod in mods.ToArray())
            {
                if (mod.Disabled)
                    continue;
                LuaLoader.Reload(mod);
                count++;
            }

            response = $"已重载 {count} 个 Lua mod";
            return true;
        }

        var reloaded = 0;
        var notFound = new List<string>();

        foreach (var raw in arguments)
        {
            var name = raw.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var mod = mods.FirstOrDefault(m =>
                string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

            if (mod is null)
            {
                notFound.Add(name);
                continue;
            }

            if (mod.Disabled)
            {
                notFound.Add($"{name}(已禁用)");
                continue;
            }

            LuaLoader.Reload(mod);
            reloaded++;
        }

        if (reloaded == 0 && notFound.Count > 0)
        {
            response = $"未找到: {string.Join(", ", notFound)}";
            return false;
        }

        response = $"已重载 {reloaded} 个 mod。";
        if (notFound.Count > 0)
            response += $"\n未找到: {string.Join(", ", notFound)}";

        Logger.Info($"[NekoLua] 通过命令重载了 {reloaded} 个 mod");
        return true;
    }
}
