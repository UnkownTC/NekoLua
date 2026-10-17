using CommandSystem;
using NekoLua.Api.Extensions;

namespace NekoLua.Api.Structures.Commands;

internal abstract class ParentCommandBase : ParentCommand
{
    public required LuaCommandSpec Spec { get; init; }
    public required LuaCommandHook Owner { get; init; }

    public override string Command => Spec.Name;
    public override string[] Aliases => Spec.Aliases;
    public override string Description => Spec.Description;

    public override void LoadGeneratedCommands() { }

    protected override bool ExecuteParent(ArraySegment<string> arguments,
                                          ICommandSender sender,
                                          out string response)
    {
        return Spec.Callback != null
            ? Owner.ExecuteCallback(Spec.Callback, arguments, sender, out response)
            : CommandExtensions.ListSubCommands(this, arguments, out response);
    }
}
