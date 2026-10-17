using MoonSharp.Interpreter;
using System.Runtime.CompilerServices;

namespace NekoLua.Api.TypeScanner;

/// <summary>
/// 缓存并管理将托管对象包装为表示 Lua 表的 DynValue，以便为相同的托管对象复用相同的包装。
/// </summary>
/// <remarks>使用 ConditionalWeakTable 缓存非 null 对象到 DynValue 的映射；对象通过 LuaObjectProxy 构建对应的表；Dispose
/// 会重置并清空缓存以释放引用。</remarks>
/// <param name="script">用于创建对象代理和对应 Lua 表的脚本上下文。</param>
public sealed class LuaObjectCache(Script script) : IDisposable
{
    private readonly Script _script = script;
    private ConditionalWeakTable<object, DynValue> _cache = new();
    /// <summary>
    /// 将托管对象包装为表示 Lua 表的 DynValue；对 null 返回 DynValue.Nil，并缓存生成的包装以便重用。
    /// </summary>
    /// <remarks>对已包装的对象使用内部缓存以返回相同的 DynValue。非 null 对象通过 LuaObjectProxy 构建对应的表并封装为 DynValue。</remarks>
    /// <param name="obj">要包装的托管对象，可为 null。</param>
    /// <returns>表示 obj 的 DynValue；obj 为 null 时返回 DynValue.Nil。</returns>
    public DynValue Wrap(object? obj)
    {
        if (obj is null)
            return DynValue.Nil;
        if (_cache.TryGetValue(obj, out var cached))
            return cached;

        var table = new LuaObjectProxy(_script, obj, this).Build();
        var dyn = DynValue.NewTable(table);
        _cache.Add(obj, dyn);
        return dyn;
    }
    /// <inheritdoc/>
    public void Dispose()
    {
        _cache = new ConditionalWeakTable<object, DynValue>();
    }
}
