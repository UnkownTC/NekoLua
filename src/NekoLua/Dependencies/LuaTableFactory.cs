using MoonSharp.Interpreter;

namespace NekoLua.Dependencies;

/// <summary>
/// 把 LabAPI 的静态集合/可空静态属性包装成 Lua 侧惯用形态的工厂。
///
/// NekoLua 的既有约定是「集合用函数取」（<c>map.doors()</c> 而不是 <c>map.doors</c>），
/// 因为 LabAPI 的这些集合是**实时快照**，每次调用都应重新枚举而不是固化在表构造期。
/// </summary>
public static class LuaTableFactory
{
    /// <summary>
    /// 惰性集合 → <c>fn() -&gt; table</c>。每次调用重新枚举 <paramref name="source"/>。
    /// </summary>
    public static DynValue Collection<T>(
        Script script,
        LuaObjectCache cache,
        Func<IEnumerable<T>> source)
    {
        return DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(script);
            var i = 1;

            foreach (var item in source())
                arr[i++] = cache.Wrap(item);

            return DynValue.NewTable(arr);
        });
    }

    /// <summary>
    /// 惰性字符串集合 → <c>fn() -&gt; table</c>。
    /// </summary>
    public static DynValue Collection(
        Script script,
        Func<IEnumerable<string>> source)
    {
        return DynValue.NewCallback((_, _) =>
            DynValue.NewTable(LuaArgs.StringArray(script, source())));
    }

    /// <summary>
    /// 惰性可空对象 → <c>fn() -&gt; 代理表 | nil</c>。
    /// </summary>
    public static DynValue Nullable<T>(
        LuaObjectCache cache,
        Func<T?> source) where T : class
    {
        return DynValue.NewCallback((_, _) =>
        {
            var v = source();
            return v is null ? DynValue.Nil : cache.Wrap(v);
        });
    }
}
