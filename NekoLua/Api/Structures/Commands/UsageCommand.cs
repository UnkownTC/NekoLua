using CommandSystem;

namespace NekoLua.Api.Structures.Commands;

internal sealed class UsageCommand : CommandBase, IUsageProvider
{
    public string[] Usage => Spec.Usages;
}
