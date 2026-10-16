using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

using MoonSharp.Interpreter;

using UnityEngine;

namespace NekoLua.Dependencies;

public sealed class LuaObjectProxy(Script script, object target, LuaObjectCache cache)
{
    private static readonly ConditionalWeakTable<Table, object> _tableToObject = new();

    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance;
    private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> PropCache = new();
    private static readonly ConcurrentDictionary<Type, Dictionary<string, MethodInfo>> MethodCache = new();
    private static readonly ConcurrentDictionary<Type, Dictionary<string, FieldInfo>> FieldCache = new();

    private static Dictionary<string, PropertyInfo> BuildPropMap(Type type)
    {
        var map = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in type.GetProperties(Flags))
        {
            if (p.GetIndexParameters().Length > 0)
                continue;
            map[p.Name] = p;
            if (!char.IsLower(p.Name[0]))
                map[char.ToLowerInvariant(p.Name[0]) + p.Name.Substring(1)] = p;
        }
        return map;
    }

    private static Dictionary<string, MethodInfo> BuildMethodMap(Type type)
    {
        var map = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in type.GetMethods(Flags))
        {
            if (m.IsSpecialName)
                continue;
            if (m.IsGenericMethodDefinition)
                continue;

            if (!map.ContainsKey(m.Name))
                map[m.Name] = m;
            if (!char.IsLower(m.Name[0]))
            {
                var camel = char.ToLowerInvariant(m.Name[0]) + m.Name.Substring(1);
                if (!map.ContainsKey(camel))
                    map[camel] = m;
            }
        }
        return map;
    }

    private static Dictionary<string, FieldInfo> BuildFieldMap(Type type)
    {
        var map = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in type.GetFields(Flags))
        {
            map[f.Name] = f;
            if (!char.IsLower(f.Name[0]))
                map[char.ToLowerInvariant(f.Name[0]) + f.Name.Substring(1)] = f;
        }
        return map;
    }

    private PropertyInfo? FindProperty(string luaName)
    {
        var map = PropCache.GetOrAdd(_type, BuildPropMap);
        map.TryGetValue(luaName, out var prop);
        return prop;
    }

    private MethodInfo? FindMethod(string luaName)
    {
        var map = MethodCache.GetOrAdd(_type, BuildMethodMap);
        map.TryGetValue(luaName, out var m);
        return m;
    }

    private FieldInfo? FindField(string luaName)
    {
        var map = FieldCache.GetOrAdd(_type, BuildFieldMap);
        map.TryGetValue(luaName, out var f);
        return f;
    }

    private readonly Script _script = script;
    private readonly object _target = target;
    private readonly Type _type = target.GetType();
    private readonly LuaObjectCache _cache = cache;

    public Table Build()
    {
        var t = new Table(_script);
        var meta = new Table(_script);

        meta["__index"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[1].Type != DataType.String)
                return DynValue.Nil;
            return GetMember(args[1].String);
        });

        meta["__newindex"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 3 || args[1].Type != DataType.String)
                return DynValue.Nil;
            SetMember(args[1].String, args[2]);
            return DynValue.Nil;
        });

        meta["__tostring"] = DynValue.NewCallback((_, _) =>
            DynValue.NewString($"[{_type.Name}: {SafeToString()}]"));

        t.MetaTable = meta;

        if (!_tableToObject.TryGetValue(t, out _))
            _tableToObject.Add(t, _target);
        return t;
    }

    private string SafeToString()
    {
        try
        { return _target.ToString() ?? ""; }
        catch { return ""; }
    }

    private DynValue GetMember(string luaName)
    {
        var prop = FindProperty(luaName);
        if (prop is { CanRead: true })
        {
            try
            { return ConvertToLua(prop.GetValue(_target)); }
            catch { return DynValue.Nil; }
        }

        var method = FindMethod(luaName);
        if (method is not null)
            return DynValue.NewCallback((_, args) => InvokeMethod(method, args));

        var field = FindField(luaName);
        if (field is not null)
        {
            try
            { return ConvertToLua(field.GetValue(_target)); }
            catch { return DynValue.Nil; }
        }

        return DynValue.Nil;
    }

    private void SetMember(string luaName, DynValue value)
    {
        var prop = FindProperty(luaName);
        if (prop is { CanWrite: true })
        {
            try
            { prop.SetValue(_target, ConvertFromLua(value, prop.PropertyType)); }
            catch (ScriptRuntimeException) { throw; }
            catch (Exception ex)
            { throw new ScriptRuntimeException($"{luaName}: {ex.Message}"); }
            return;
        }

        var field = FindField(luaName);
        if (field is { IsInitOnly: false })
        {
            try
            { field.SetValue(_target, ConvertFromLua(value, field.FieldType)); }
            catch (ScriptRuntimeException) { throw; }
            catch (Exception ex)
            { throw new ScriptRuntimeException($"{luaName}: {ex.Message}"); }
            return;
        }

        throw new ScriptRuntimeException($"字段 '{luaName}' 不存在或只读");
    }
    private DynValue InvokeMethod(MethodInfo method, CallbackArguments args)
    {
        var parameters = method.GetParameters();
        var callArgs = new object?[parameters.Length];
        var outIndexes = new List<int>();

        for (var i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].IsOut)
            {
                var elem = parameters[i].ParameterType.GetElementType()!;
                callArgs[i] = elem.IsValueType ? Activator.CreateInstance(elem) : null;
                outIndexes.Add(i);
                continue;
            }

            var luaIdx = i + 1;

            if (args.Count <= luaIdx)
            {
                callArgs[i] = parameters[i].HasDefaultValue
                    ? parameters[i].DefaultValue
                    : parameters[i].ParameterType.IsValueType
                        ? Activator.CreateInstance(parameters[i].ParameterType)
                        : null;
                continue;
            }

            callArgs[i] = ConvertFromLua(args[luaIdx], parameters[i].ParameterType);
        }

        try
        {
            var result = method.Invoke(_target, callArgs);

            if (outIndexes.Count == 0)
                return ConvertToLua(result);

            var all = new DynValue[outIndexes.Count + 1];
            all[0] = ConvertToLua(result);
            for (var k = 0; k < outIndexes.Count; k++)
                all[k + 1] = ConvertToLua(callArgs[outIndexes[k]]);
            return DynValue.NewTuple(all);
        }
        catch (TargetInvocationException ex)
        {
            throw new ScriptRuntimeException(
                $"{method.Name}: {ex.InnerException?.Message ?? ex.Message}");
        }
        catch (ScriptRuntimeException) { throw; }
        catch (Exception ex)
        {
            throw new ScriptRuntimeException($"{method.Name}: {ex.Message}");
        }
    }
    private DynValue ConvertToLua(object? value)
    {
        if (value is null)
            return DynValue.Nil;

        return value switch
        {
            string s => DynValue.NewString(s),
            bool b => DynValue.NewBoolean(b),
            char c => DynValue.NewString(c.ToString()),
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
            decimal x => DynValue.NewNumber((double)x),

            Vector2 v => DynValue.NewTable(LuaVector.PackV2(_script, v)),
            Vector3 v => DynValue.NewTable(LuaVector.PackV3(_script, v)),
            Quaternion q => DynValue.NewTable(LuaVector.PackQ(_script, q)),

            DateTime dt => DynValue.NewNumber(
                new DateTimeOffset(dt).ToUnixTimeMilliseconds() / 1000.0),
            TimeSpan ts => DynValue.NewNumber(ts.TotalSeconds),

            Version ver => DynValue.NewTable(new Table(_script)
            {
                ["major"] = ver.Major,
                ["minor"] = ver.Minor,
                ["build"] = ver.Build,
                ["revision"] = ver.Revision,
            }),
            Bounds b => DynValue.NewTable(LuaVector.PackBounds(_script, b)),
            Enum e => PackEnum(e),
            Color c => DynValue.NewTable(LuaVector.PackColor(_script, c)),
            Delegate => DynValue.Nil,

            IDictionary dict => PackDictionary(dict),
            IEnumerable coll => PackCollection(coll),

            _ => PackReferenceOrDefault(value),
        };
    }
    private DynValue PackReferenceOrDefault(object value)
    {
        var vt = value.GetType();
        if (vt.IsValueType)
            return PackStruct(value);

        return _cache.Wrap(value);
    }
    private const int MaxCollectionSize = 512;
    private DynValue PackDictionary(IDictionary dict)
    {
        var t = new Table(_script);
        foreach (DictionaryEntry entry in dict)
            t[ConvertToLua(entry.Key)] = ConvertToLua(entry.Value);
        return DynValue.NewTable(t);
    }
    private DynValue PackCollection(IEnumerable coll)
    {
        var t = new Table(_script);
        var i = 1;
        var truncated = false;

        foreach (var item in coll)
        {
            if (i > MaxCollectionSize)
            { truncated = true; break; }
            t[i++] = ConvertToLua(item);
        }

        if (truncated)
            LabApi.Features.Console.Logger.Warn(
                $"集合超过 {MaxCollectionSize} 个元素，已截断");

        return DynValue.NewTable(t);
    }

    private DynValue PackStruct(object value)
    {
        var type = value.GetType();
        var t = new Table(_script);

        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0)
                continue;
            if (!p.CanRead)
                continue;

            try
            { t[p.Name] = ConvertToLua(p.GetValue(value)); }
            catch { }
        }

        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            try
            { t[f.Name] = ConvertToLua(f.GetValue(value)); }
            catch { }
        }

        return DynValue.NewTable(t);
    }

    private static DynValue PackEnum(Enum e)
    {
        var type = e.GetType();
        return type.IsDefined(typeof(FlagsAttribute), false)
            ? DynValue.NewNumber(Convert.ToInt64(e))
            : DynValue.NewString(e.ToString().ToLowerInvariant());
    }
    private static object? ConvertFromLua(DynValue v, Type target)
    {
        if (target == typeof(string))
        {
            if (v.Type == DataType.Nil)
                return null;
            if (v.Type != DataType.String)
                throw new ScriptRuntimeException("需要字符串");
            return v.String;
        }

        if (target == typeof(bool))
        {
            if (v.Type != DataType.Boolean)
                throw new ScriptRuntimeException("需要布尔");
            return v.Boolean;
        }

        if (target == typeof(char))
        {
            if (v.Type != DataType.String || v.String.Length != 1)
                throw new ScriptRuntimeException("需要单字符");
            return v.String[0];
        }

        if (target.IsEnum)
        {
            if (v.Type == DataType.String)
                return Enum.Parse(target, v.String, true);
            if (v.Type == DataType.Number)
                return Enum.ToObject(target, (long)v.Number);
            throw new ScriptRuntimeException("需要枚举字符串或数字");
        }

        if (target == typeof(Vector3))
            return LuaVector.UnpackV3Args(v, "参数");
        if (target == typeof(Vector2))
            return LuaVector.UnpackV2Args(v, "参数");
        if (target == typeof(Quaternion))
            return LuaVector.UnpackQArgs(v, "参数");

        if (target == typeof(DateTime))
        {
            if (v.Type != DataType.Number)
                throw new ScriptRuntimeException("需要 Unix 秒");
            return DateTimeOffset.FromUnixTimeMilliseconds((long)(v.Number * 1000))
                .UtcDateTime;
        }

        if (target == typeof(TimeSpan))
        {
            if (v.Type != DataType.Number)
                throw new ScriptRuntimeException("需要秒数");
            return TimeSpan.FromSeconds(v.Number);
        }

        if (target == typeof(Bounds))
            return LuaVector.UnpackBoundsArgs(v, "参数");
        if (target == typeof(Color))
            return LuaVector.UnpackColorArgs(v, "参数");

        if (v.Type == DataType.Number)
        {
            var d = v.Number;
            if (target == typeof(byte))
                return (byte)d;
            if (target == typeof(sbyte))
                return (sbyte)d;
            if (target == typeof(short))
                return (short)d;
            if (target == typeof(ushort))
                return (ushort)d;
            if (target == typeof(int))
                return (int)d;
            if (target == typeof(uint))
                return (uint)d;
            if (target == typeof(long))
                return (long)d;
            if (target == typeof(ulong))
                return (ulong)d;
            if (target == typeof(float))
                return (float)d;
            if (target == typeof(double))
                return d;
            if (target == typeof(decimal))
                return (decimal)d;
        }

        if (target.IsArray && v.Type == DataType.Table)
        {
            var elem = target.GetElementType()!;
            var tbl = v.Table;
            var n = tbl.Length;

            var arr = Array.CreateInstance(elem, n);
            for (var i = 1; i <= n; i++)
                arr.SetValue(ConvertFromLua(tbl.Get(i), elem), i - 1);
            return arr;
        }
        if (v.Type == DataType.Table && !target.IsValueType)
        {
            if (_tableToObject.TryGetValue(v.Table, out var obj)
                && target.IsAssignableFrom(obj.GetType()))
            {
                return obj;
            }
        }

        if (v.Type == DataType.Nil)
            return target.IsValueType ? Activator.CreateInstance(target) : null;

        throw new ScriptRuntimeException($"不支持从 Lua 转换到 {target.Name}");
    }
    public static object? Unwrap(Table t)
    => _tableToObject.TryGetValue(t, out var o) ? o : null;
}
