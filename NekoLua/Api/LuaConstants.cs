namespace NekoLua.Api;


/// <summary>
/// Lua 子内容常量类。
/// </summary>
public static class LuaConstants
{
    /// <summary>
    /// 深拷贝的最大数量，当超过此数量时便不进行序列化，防止栈溢出。
    /// </summary>
    public const int MaxDepth = 512;
}
