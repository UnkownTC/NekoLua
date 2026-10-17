using CommandSystem;

namespace NekoLua.Api.Structures.Commands;

internal sealed class HiddenUsageCommand : CommandBase, IHiddenCommand, IUsageProvider
{
    public string[] Usage => Spec.Usages;
}
