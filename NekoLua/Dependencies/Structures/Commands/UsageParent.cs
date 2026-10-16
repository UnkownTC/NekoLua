using CommandSystem;

namespace NekoLua.Dependencies.Structures.Commands;

internal sealed class UsageParent : ParentCommandBase, IUsageProvider
{
    public string[] Usage => Spec.Usages;
}
