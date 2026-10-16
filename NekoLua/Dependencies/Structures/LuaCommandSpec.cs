using MoonSharp.Interpreter;

namespace NekoLua.Dependencies.Structures;

public sealed class LuaCommandSpec
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string[] Aliases { get; init; }
    public required bool IsHide { get; init; }
    public required string[] Usages { get; init; }
    public DynValue? Callback { get; init; }
}
