using CommandSystem;

namespace NekoLua.Dependencies.Structures.Commands;

internal sealed class HiddenUsageParent : ParentCommandBase, IHiddenCommand, IUsageProvider
{
    public string[] Usage => Spec.Usages;
}
