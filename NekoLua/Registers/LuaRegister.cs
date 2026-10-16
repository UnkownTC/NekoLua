using System.Reflection;

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
