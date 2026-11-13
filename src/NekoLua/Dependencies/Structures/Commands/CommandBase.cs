using CommandSystem;

namespace NekoLua.Dependencies.Structures.Commands;

internal abstract class CommandBase : ICommand
{
    public required LuaCommandSpec Spec { get; init; }
    public required LuaCommandHook Owner { get; init; }

    public string Command => Spec.Name;
    public string[] Aliases => Spec.Aliases;
    public string Description => Spec.Description;

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        return Owner.ExecuteCallback(Spec.Callback!, arguments, sender, out response);
    }
}
