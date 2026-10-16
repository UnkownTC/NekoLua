using System;
using System.Collections.Generic;
using System.Linq;

using CommandSystem;

using LabApi.Features.Console;

using NekoLua.Loader;

namespace NekoLua.Commands;

[CommandHandler(typeof(NekoLuaParentCommand))]
internal class RefreshCommand : ICommand
{
    public string Command => "refresh";
    public string[] Aliases => ["rf", "scan"];
    public string Description => "重新扫描 mods 目录，加载新增/清理已删除的 mod";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (!sender.CheckPermission(PlayerPermissions.ServerConsoleCommands))
        {
            response =
                $"权限不足！ 需要拥有权限：\n" +
                $"  - ServerConsoleCommands";
            return false;
        }

        var before = LuaLoader.Mods.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<LuaModInstance> after;
        try
        {
            after = LuaLoader.Refresh();
        }
        catch (Exception ex)
        {
            Logger.Error($"[NekoLua] refresh 失败: {ex.Message}");
            response = $"刷新失败: {ex.Message}";
            return false;
        }

        var afterNames = after.Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = afterNames.Except(before, StringComparer.OrdinalIgnoreCase).ToList();
        var removed = before.Except(afterNames, StringComparer.OrdinalIgnoreCase).ToList();

        var lines = new List<string>
        {
            $"扫描完成，当前共 {after.Count} 个 mod。"
        };

        if (added.Count > 0)
            lines.Add($"新增 ({added.Count}): {string.Join(", ", added)}");

        if (removed.Count > 0)
            lines.Add($"移除 ({removed.Count}): {string.Join(", ", removed)}");

        if (added.Count == 0 && removed.Count == 0)
            lines.Add("无变化。");

        response = string.Join("\n", lines);

        Logger.Info($"[NekoLua] refresh: +{added.Count} / -{removed.Count} / 共 {after.Count}");
        return true;
    }
}
