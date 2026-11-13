using System.Runtime.CompilerServices;

using MoonSharp.Interpreter;

namespace NekoLua.Loader;

public class LuaModInstance
{
    public string Name { get; init; }
    public string ModDir { get; init; }
    public string EntryFile { get; init; }
    public Script Script { get; init; }
    public bool Disabled { get; set; }
    public static bool operator ==(LuaModInstance? a, LuaModInstance? b)
    {
        return ReferenceEquals(a, b);
    }
    public static bool operator !=(LuaModInstance? a, LuaModInstance? b)
    {
        return !ReferenceEquals(a, b);
    }
    // 恒等语义：本类型被 LuaNetworkManager / LuaRegexProxy 用作字典键，
    // 按字段值比较会让同引用键查找失败（reload 后新旧实例视为相同还会串缓存），
    // 必须与 GetHashCode 的引用哈希保持同一契约（审查 P1-3）
    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj);
    }
    public override int GetHashCode()
    {
        return RuntimeHelpers.GetHashCode(this);
    }
    public override string ToString()
    {
        return base.ToString()!;
    }
}
