using System.Runtime.CompilerServices;

using MoonSharp.Interpreter;

namespace NekoLua.Dependencies;

public sealed class LuaObjectCache(Script script) : IDisposable
{
    private readonly Script _script = script;
    private ConditionalWeakTable<object, DynValue> _cache = new();
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
    public void Dispose()
    {
        _cache = new ConditionalWeakTable<object, DynValue>();
    }
}
