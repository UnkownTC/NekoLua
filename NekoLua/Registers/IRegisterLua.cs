using MoonSharp.Interpreter;

using NekoLua.Loader;

namespace NekoLua.Registers;

/// <summary>
/// 表示可注册到 Lua 的工厂，提供导出到 Lua 的名称并创建对应的 Lua 表。
/// </summary>
/// <remarks>实现应返回可直接注册到 Lua 环境的 Table；Name 用于在注册时作为标识。实现需注意创建期间的资源管理与线程安全。</remarks>
public interface IRegisterLua
{
    /// <summary>
    /// 此 Lua 类的名称。
    /// </summary>
    string Name { get; }
    /// <summary>
    /// 为指定的 LuaModInstance 创建 Lua 类。
    /// </summary>
    /// <param name="mod">要关联的 Lua 模组实例，用于初始化或配置返回的 Table。</param>
    /// <returns>已创建的 Table 实例。</returns>
    Table Create(LuaModInstance mod);
}
