using CommandSystem;

namespace NekoLua.Dependencies.Structures.Commands;

internal sealed class UsageCommand : CommandBase, IUsageProvider
{
    public string[] Usage => Spec.Usages;
}
