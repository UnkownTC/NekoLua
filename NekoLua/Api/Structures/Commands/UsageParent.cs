using CommandSystem;

namespace NekoLua.Api.Structures.Commands;

internal sealed class UsageParent : ParentCommandBase, IUsageProvider
{
    public string[] Usage => Spec.Usages;
}
