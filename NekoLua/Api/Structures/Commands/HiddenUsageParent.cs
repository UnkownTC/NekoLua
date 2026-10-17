using CommandSystem;

namespace NekoLua.Api.Structures.Commands;

internal sealed class HiddenUsageParent : ParentCommandBase, IHiddenCommand, IUsageProvider
{
    public string[] Usage => Spec.Usages;
}
