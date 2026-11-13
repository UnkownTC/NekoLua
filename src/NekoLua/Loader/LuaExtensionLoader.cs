using System.Reflection;

using LabApi.Features.Console;

using NekoLua.Registers;

namespace NekoLua.Loader;

/// <summary>
/// 扩展程序集装载器：扫描 <see cref="LuaPaths.Extensions"/> 下的 DLL，
/// 实例化其中的 <see cref="IRegisterLua"/>（新增 Lua 全局表）与 <see cref="ILuaExtension"/>（生命周期）。
/// </summary>
/// <remarks>
/// <para>单个 DLL / 单个类型失败只记日志，不影响主插件与其它扩展。</para>
/// <para>程序集一旦加载便无法卸载，因此这里只负责「装配」；真正的状态回收由扩展在
/// <see cref="ILuaExtension.OnUnload"/> 中完成。</para>
/// </remarks>
public static class LuaExtensionLoader
{
    private static readonly List<Assembly> _assemblies = [];
    private static readonly List<ILuaExtension> _extensions = [];

    /// <summary>本次装载涉及的扩展程序集（诊断用）。</summary>
    public static IReadOnlyList<Assembly> Assemblies => _assemblies;

    /// <summary>已装载的扩展生命周期实例。</summary>
    public static IReadOnlyList<ILuaExtension> Extensions => _extensions;

    /// <summary>扫描扩展目录并完成装配；目录不存在时自动创建。</summary>
    public static void LoadAll()
    {
        var dir = LuaPaths.Extensions;
        if (!dir.Exists)
        {
            dir.Create();
            return;
        }

        HashSet<string> knownIds = new(StringComparer.OrdinalIgnoreCase);

        foreach (var file in dir.GetFiles("*.dll").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            Assembly asm;
            try
            {
                asm = Assembly.LoadFrom(file.FullName);
            }
            catch (Exception ex)
            {
                Logger.Error($"[NekoLua] 扩展程序集加载失败 {file.Name}: {ex.Message}");
                continue;
            }

            _assemblies.Add(asm);

            foreach (var type in GetLoadableTypes(asm))
            {
                TryRegisterTable(type);
                TryAddExtension(type, knownIds);
            }
        }

        foreach (var extension in _extensions)
        {
            try
            {
                extension.OnLoad();
            }
            catch (Exception ex)
            {
                Logger.Error($"[NekoLua] 扩展 {extension.Id} OnLoad 失败: {ex.Message}");
            }
        }

        if (_assemblies.Count > 0)
            Logger.Info($"[NekoLua] 已装载 {_assemblies.Count} 个扩展程序集、{_extensions.Count} 个生命周期扩展");
    }

    /// <summary>逆序调用扩展的 <see cref="ILuaExtension.OnUnload"/> 并清空装载记录。</summary>
    public static void UnloadAll()
    {
        for (var i = _extensions.Count - 1; i >= 0; i--)
        {
            try
            {
                _extensions[i].OnUnload();
            }
            catch (Exception ex)
            {
                Logger.Error($"[NekoLua] 扩展 {_extensions[i].Id} OnUnload 失败: {ex.Message}");
            }
        }

        _extensions.Clear();
        _assemblies.Clear();
    }

    private static void TryRegisterTable(Type type)
    {
        if (!type.IsClass || type.IsAbstract || type.IsInterface || type.IsValueType)
            return;

        if (!typeof(IRegisterLua).IsAssignableFrom(type))
            return;

        if (type.GetConstructor(Type.EmptyTypes) is null)
            return;

        try
        {
            if (Activator.CreateInstance(type) is IRegisterLua register)
                _ = LuaRegister.RegisterInstance(register);
        }
        catch (Exception ex)
        {
            Logger.Error($"[NekoLua] 扩展表 {type.FullName} 实例化失败: {ex.Message}");
        }
    }

    private static void TryAddExtension(Type type, HashSet<string> knownIds)
    {
        if (!type.IsClass || type.IsAbstract || type.IsInterface || type.IsValueType)
            return;

        if (!typeof(ILuaExtension).IsAssignableFrom(type))
            return;

        if (type.GetConstructor(Type.EmptyTypes) is null)
            return;

        try
        {
            if (Activator.CreateInstance(type) is not ILuaExtension extension)
                return;

            var id = extension.Id;
            if (string.IsNullOrWhiteSpace(id))
            {
                Logger.Error($"[NekoLua] 扩展 {type.FullName} 的 Id 为空，已跳过");
                return;
            }

            if (!knownIds.Add(id))
            {
                Logger.Error($"[NekoLua] 扩展 Id '{id}' 重复，{type.FullName} 已跳过");
                return;
            }

            _extensions.Add(extension);
        }
        catch (Exception ex)
        {
            Logger.Error($"[NekoLua] 扩展 {type.FullName} 实例化失败: {ex.Message}");
        }
    }

    private static Type[] GetLoadableTypes(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            foreach (var loaderException in ex.LoaderExceptions)
                Logger.Warn($"[NekoLua] 扩展 {asm.GetName().Name} 部分类型不可用: {loaderException?.Message}");
            return ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }
        catch (Exception ex)
        {
            Logger.Error($"[NekoLua] 无法枚举扩展 {asm.GetName().Name} 的类型: {ex.Message}");
            return [];
        }
    }
}
