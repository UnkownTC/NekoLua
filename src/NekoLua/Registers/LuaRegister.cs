using System.Reflection;

using LabApi.Features.Console;

namespace NekoLua.Registers;

public static class LuaRegister
{
    private static readonly List<IRegisterLua> _registers = [];
    public static IReadOnlyCollection<IRegisterLua> Registers => _registers;
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

            if (type.GetConstructor(Type.EmptyTypes) is null)
                continue;

            var instance = Activator.CreateInstance(type);
            if (instance is IRegisterLua registerLua)
            {
                _ = RegisterInstance(registerLua);
            }
        }
    }

    /// <summary>
    /// 注册单个全局表提供者（扩展程序集入口）。
    /// </summary>
    /// <param name="register">表提供者实例。</param>
    /// <returns>注册成功返回 <see langword="true"/>；名称为空或与已有表重名时返回 <see langword="false"/>。</returns>
    /// <remarks>
    /// 脚本可见的全局名必须唯一：同名表若后注册覆盖前者，脚本行为会随扩展装载顺序漂移，
    /// 因此这里直接拒绝并记录错误，而不是静默覆盖。
    /// </remarks>
    public static bool RegisterInstance(IRegisterLua register)
    {
        if (register is null)
            return false;

        var name = register.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            Logger.Error($"[NekoLua] 全局表注册器 {register.GetType().FullName} 的 Name 为空，已跳过");
            return false;
        }

        foreach (var existed in _registers)
        {
            if (string.Equals(existed.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                Logger.Error(
                    $"[NekoLua] 全局表名 '{name}' 已被 {existed.GetType().FullName} 占用，" +
                    $"{register.GetType().FullName} 注册被拒绝（不允许同名覆盖）");
                return false;
            }
        }

        _registers.Add(register);
        return true;
    }

    internal static void Clear()
    {
        _registers.Clear();
    }
}
