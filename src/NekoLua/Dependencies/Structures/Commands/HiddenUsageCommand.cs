using CommandSystem;

namespace NekoLua.Dependencies.Structures.Commands;

internal sealed class HiddenUsageCommand : CommandBase, IHiddenCommand, IUsageProvider
{
    public string[] Usage => Spec.Usages;
}
