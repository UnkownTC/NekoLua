using System.Reflection;

namespace NekoLua.Registers;

/// <summary>
/// 管理 IRegisterLua 实例的发现、实例化与注册，并提供对已注册实例的只读访问。
/// </summary>
/// <remarks>扫描指定程序集的类型，实例化所有非抽象且实现 IRegisterLua 的类并将实例添加到内部集合。Registers 属性公开该集合的只读视图。内部 Clear
/// 方法用于清空已注册项。注册不做去重，且对并发访问未做同步保护。</remarks>
public static class LuaRegister
{
    private static readonly List<IRegisterLua> _registers = [];
    /// <summary>
    /// 当前已注册的 IRegisterLua 实例的只读集合。
    /// </summary>
    public static IReadOnlyCollection<IRegisterLua> Registers => _registers;
    /// <summary>
    /// 扫描指定程序集，查找所有非抽象、接口、值类型且实现 IRegisterLua 的类并注册。
    /// </summary>
    /// <remarks>使用 Activator.CreateInstance 调用无参构造函数创建实例；创建成功的实例会添加到 _registers
    /// 集合；实例创建失败或构造函数抛出的异常将向上抛出。</remarks>
    /// <param name="asm">要扫描以查找实现 IRegisterLua 类型的程序集。</param>
    public static void Register(Assembly asm)
    {
        foreach (var type in asm.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || type.IsValueType)
                continue;

            if (!type.IsClass)
                continue;

            if (!typeof(IRegisterLua).IsAssignableFrom(type))
                continue;

            var instance = Activator.CreateInstance(type);
            if (instance is IRegisterLua registerLua)
            {
                _registers.Add(registerLua);
            }
        }
    }
    internal static void Clear()
    {
        _registers.Clear();
    }
}
