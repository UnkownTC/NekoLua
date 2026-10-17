using NorthwoodLib.Pools;

namespace NekoLua.Api.Extensions;

internal static class CommandExtensions
{
    public static bool ListSubCommands(ParentCommand parent, ArraySegment<string> arguments, out string response)
    {
        var stringBuilder = StringBuilderPool.Shared.Rent();
        stringBuilder.AppendLine((arguments.Count > 0) ? ("'" + arguments.At(0) + "' 不是子指令!") : "你需要执行此指令的子指令!");
        stringBuilder.AppendLine("子指令如下：");
        foreach (var allCommand in parent.AllCommands)
        {
            stringBuilder.AppendLine("    " + allCommand.Command + " - " + allCommand.Description + " 别名 [" + string.Join(" ", allCommand.Aliases) + "]");
        }

        response = StringBuilderPool.Shared.ToStringReturn(stringBuilder);
        return arguments.Count == 0;
    }
}
