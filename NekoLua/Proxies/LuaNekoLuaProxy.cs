using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;
using System.Reflection;

namespace NekoLua.Proxies;

/// <summary>
/// nekolua 表：
///   nekolua.name / version / author / description
///   nekolua.config                  -- 配置快照（反射自动扫描 Config 所有字段，转 camelCase）
/// </summary>
internal class LuaNekoLuaProxy : IRegisterLua
{
    public string Name { get; } = "nekolua";
    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var plugin = MainPlugin.Singleton;

        var t = new Table(script)
        {
            ["name"] = plugin?.Name ?? "NekoLua",
            ["version"] = GetVersion(),
            ["author"] = plugin?.Author ?? "",
            ["description"] = plugin?.Description ?? "",
            ["config"] = DynValue.NewTable(BuildConfig(script, MainPlugin.CacheConfig)),
        };

        return t;
    }
    static string GetVersion()
    {
        var v = MainPlugin.PluginAssembly?.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
    static Table BuildConfig(Script script, Config? cfg)
    {
        var t = new Table(script);
        if (cfg is null)
            return t;

        foreach (var p in typeof(Config).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead)
                continue;
            if (p.GetIndexParameters().Length > 0)
                continue;

            t[ToCamel(p.Name)] = ToLua(script, p.GetValue(cfg));
        }

        return t;
    }
    static string ToCamel(string name)
    {
        return string.IsNullOrEmpty(name) || char.IsLower(name[0])
                ? name
                : char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    static DynValue ToLua(Script script, object? value)
    {
        return value is null
            ? DynValue.Nil
            : value switch
            {
                string s => DynValue.NewString(s),
                bool b => DynValue.NewBoolean(b),
                byte x => DynValue.NewNumber(x),
                sbyte x => DynValue.NewNumber(x),
                short x => DynValue.NewNumber(x),
                ushort x => DynValue.NewNumber(x),
                int x => DynValue.NewNumber(x),
                uint x => DynValue.NewNumber(x),
                long x => DynValue.NewNumber(x),
                ulong x => DynValue.NewNumber(x),
                float x => DynValue.NewNumber(x),
                double x => DynValue.NewNumber(x),

                string[] arr => ToLuaArray(script, arr, DynValue.NewString),

                _ => DynValue.Nil,
            };
    }
    static DynValue ToLuaArray<T>(Script script, T[] arr, Func<T, DynValue> toLua)
    {
        var t = new Table(script);
        for (var i = 0; i < arr.Length; i++)
            t[i + 1] = toLua(arr[i]);
        return DynValue.NewTable(t);
    }
}
