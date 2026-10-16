using System.Collections.Concurrent;
using System.Reflection;

using MoonSharp.Interpreter;

using UnityEngine;

namespace NekoLua.Dependencies;

public sealed class LuaTypeMapper(Script script)
{
    private static readonly HashSet<Type> SimpleTypes =
    [
        typeof(bool),
        typeof(byte), typeof(sbyte),
        typeof(short), typeof(ushort),
        typeof(int), typeof(uint),
        typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal),
        typeof(char),
        typeof(string),
        typeof(Vector2), typeof(Vector3), typeof(Quaternion),
        typeof(DateTime),
        typeof(TimeSpan),
        typeof(Version),
        typeof(Bounds),
        typeof(Color),
    ];
    private static readonly ConcurrentDictionary<(Type, BindingFlags), PropertyInfo[]> _propListCache = new();
    private static readonly ConcurrentDictionary<string, string> _camelCache = new();
    private static PropertyInfo[] GetFilteredProperties(Type type, BindingFlags flags)
    {
        return _propListCache.GetOrAdd((type, flags), static key =>
        {
            var (t, f) = key;
            var list = new List<PropertyInfo>();

            foreach (var p in t.GetProperties(f))
            {
                if (p.GetIndexParameters().Length > 0)
                    continue;
                if (!p.CanRead)
                    continue;
                list.Add(p);
            }

            return [.. list];
        });
    }

    private static string ToCamel(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        return _camelCache.GetOrAdd(name, static n =>
        {
            if (char.IsLower(n[0]))
                return n;
            return char.ToLowerInvariant(n[0]) + n.Substring(1);
        });
    }
    private sealed class Prop
    {
        public required Func<DynValue> Getter;
        public Action<DynValue>? Setter;
    }

    private readonly Script _script = script;
    private readonly Dictionary<string, Prop> _props = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ignored = new(StringComparer.Ordinal);
    private LuaObjectCache? _objectCache;
    private bool _useProxy;
    private string _prefix = "";

    public LuaTypeMapper UseProxy()
    {
        _useProxy = true;
        return this;
    }

    public LuaTypeMapper Prefix(string prefix)
    {
        _prefix = string.IsNullOrEmpty(prefix) ? "" : prefix.TrimEnd('.') + ".";
        return this;
    }

    public LuaTypeMapper Ignore(params string[] csharpNames)
    {
        foreach (var n in csharpNames)
            _ignored.Add(n);
        return this;
    }

    private LuaObjectCache ProxyCache => _objectCache ??= new LuaObjectCache(_script);
    public LuaTypeMapper Scan(Type type) => ScanInternal(type, null);
    public LuaTypeMapper ScanInstance(object instance) => ScanInternal(instance.GetType(), instance);

    private LuaTypeMapper ScanInternal(Type type, object? instance)
    {
        var flags = instance is null
            ? BindingFlags.Public | BindingFlags.Static
            : BindingFlags.Public | BindingFlags.Instance;

        var properties = GetFilteredProperties(type, flags);

        foreach (var p in properties)
        {
            if (_ignored.Contains(p.Name))
                continue;

            var isSimple = SimpleTypes.Contains(p.PropertyType) || p.PropertyType.IsEnum;
            if (!isSimple && !_useProxy)
                continue;

            var luaName = ToCamel(p.Name);
            if (_props.ContainsKey(luaName))
                continue;

            var prop = p;
            var inst = instance;

            DynValue getter()
            {
                try
                { return ToDyn(prop.GetValue(inst)); }
                catch { return DynValue.Nil; }
            }

            Action<DynValue>? setter = null;
            if (prop.CanWrite)
            {
                setter = v =>
                {
                    try
                    { prop.SetValue(inst, FromDyn(v, prop.PropertyType, luaName)); }
                    catch (ScriptRuntimeException) { throw; }
                    catch (Exception ex)
                    {
                        throw new ScriptRuntimeException($"{_prefix}{luaName}: {ex.Message}");
                    }
                };
            }

            _props[luaName] = new Prop { Getter = getter, Setter = setter };
        }
        return this;
    }
    public LuaTypeMapper Custom(string luaName, Func<DynValue> getter, Action<DynValue>? setter = null)
    {
        _props[luaName] = new Prop { Getter = getter, Setter = setter };
        return this;
    }
    public Table Build()
    {
        var t = new Table(_script);
        var meta = new Table(_script);

        meta["__index"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[1].Type != DataType.String)
                return DynValue.Nil;

            return _props.TryGetValue(args[1].String, out var p)
                ? p.Getter()
                : DynValue.Nil;
        });

        meta["__newindex"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 3 || args[1].Type != DataType.String)
                return DynValue.Nil;

            var name = args[1].String;

            if (!_props.TryGetValue(name, out var p))
                throw new ScriptRuntimeException($"{_prefix}字段 '{name}' 不存在");

            if (p.Setter is null)
                throw new ScriptRuntimeException($"{_prefix}字段 '{name}' 只读");

            p.Setter(args[2]);
            return DynValue.Nil;
        });

        t.MetaTable = meta;
        return t;
    }

    private DynValue ToDyn(object? value) => value switch
    {

        null => DynValue.Nil,
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
        DateTime dt => DynValue.NewNumber(ToUnixSeconds(dt)),
        TimeSpan ts => DynValue.NewNumber(ts.TotalSeconds),
        Version v => DynValue.NewTable(new Table(_script)
        {
            ["major"] = v.Major,
            ["minor"] = v.Minor,
            ["build"] = v.Build,
            ["revision"] = v.Revision,
        }),
        Bounds b => DynValue.NewTable(LuaVector.PackBounds(_script, b)),
        Enum e => PackEnum(e),
        Color c => DynValue.NewTable(LuaVector.PackColor(_script, c)),

        _ => _useProxy && value is not null
            ? ProxyCache.Wrap(value)
            : DynValue.Nil,
    };

    private static object? FromDyn(DynValue v, Type target, string key)
    {
        if (target == typeof(string))
        {
            if (v.Type != DataType.String)
                throw new ScriptRuntimeException($"{key}: 需要字符串");
            return v.String;
        }

        if (target == typeof(bool))
        {
            if (v.Type != DataType.Boolean)
                throw new ScriptRuntimeException($"{key}: 需要布尔");
            return v.Boolean;
        }

        if (target == typeof(char))
        {
            if (v.Type != DataType.String || v.String.Length != 1)
                throw new ScriptRuntimeException($"{key}: 需要单字符");
            return v.String[0];
        }

        if (target.IsEnum)
            return UnpackEnum(v, target, key);

        if (target == typeof(Vector2))
            return LuaVector.UnpackV2Args(v, key);

        if (target == typeof(Vector3))
            return LuaVector.UnpackV3Args(v, key);

        if (target == typeof(Quaternion))
            return LuaVector.UnpackQArgs(v, key);

        if (target == typeof(DateTime))
        {
            if (v.Type != DataType.Number)
                throw new ScriptRuntimeException($"{key}: 需要数字（Unix 秒）");
            return DateTimeOffset.FromUnixTimeMilliseconds((long)(v.Number * 1000))
                .UtcDateTime;
        }

        if (target == typeof(TimeSpan))
        {
            if (v.Type != DataType.Number)
                throw new ScriptRuntimeException($"{key}: 需要数字（秒）");
            return TimeSpan.FromSeconds(v.Number);
        }

        if (target == typeof(Version))
        {
            if (v.Type == DataType.String)
            {
                if (!Version.TryParse(v.String, out var parsed))
                    throw new ScriptRuntimeException($"{key}: 非法版本号 '{v.String}'");
                return parsed;
            }

            if (v.Type == DataType.Table)
            {
                var t = v.Table;
                var major = GetInt(t, "major", 0);
                var minor = GetInt(t, "minor", 0);
                var build = GetInt(t, "build", -1);
                var rev = GetInt(t, "revision", -1);

                return build < 0
                    ? new Version(major, minor)
                    : rev < 0
                        ? new Version(major, minor, build)
                        : new Version(major, minor, build, rev);
            }

            throw new ScriptRuntimeException($"{key}: 需要版本 table 或字符串");
        }
        if (target == typeof(Bounds))
            return LuaVector.UnpackBoundsArgs(v, key);
        if (target == typeof(Color))
            return LuaVector.UnpackColorArgs(v, key);

        if (v.Type != DataType.Number)
            throw new ScriptRuntimeException($"{key}: 需要数字");

        var d = v.Number;
        return Type.GetTypeCode(target) switch
        {
            TypeCode.Byte => (byte)d,
            TypeCode.SByte => (sbyte)d,
            TypeCode.Int16 => (short)d,
            TypeCode.UInt16 => (ushort)d,
            TypeCode.Int32 => (int)d,
            TypeCode.UInt32 => (uint)d,
            TypeCode.Int64 => (long)d,
            TypeCode.UInt64 => (ulong)d,
            TypeCode.Single => (float)d,
            TypeCode.Double => d,
            TypeCode.Decimal => (decimal)d,
            _ => throw new ScriptRuntimeException($"{key}: 不支持的类型"),
        };
    }
    private static double ToUnixSeconds(DateTime dt)
    {
        var dto = dt.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Local))
            : new DateTimeOffset(dt);

        return dto.ToUnixTimeMilliseconds() / 1000.0;
    }

    private static DynValue PackEnum(Enum e)
    {
        var t = e.GetType();

        return t.IsDefined(typeof(FlagsAttribute), false)
            ? DynValue.NewNumber(Convert.ToInt64(e))
            : DynValue.NewString(e.ToString().ToLowerInvariant());
    }

    private static object UnpackEnum(DynValue v, Type target, string key)
    {
        var isFlags = target.IsDefined(typeof(FlagsAttribute), false);

        if (isFlags && v.Type == DataType.Number)
            return Enum.ToObject(target, (long)v.Number);

        if (v.Type == DataType.String)
        {
            try
            {
                return Enum.Parse(target, v.String, ignoreCase: true);
            }
            catch
            {
                var valid = string.Join(" / ",
                    Enum.GetNames(target).Select(n => isFlags ? n : n.ToLowerInvariant()));
                throw new ScriptRuntimeException(
                    $"{key}: 未知枚举值 '{v.String}'，可选: {valid}");
            }
        }
        if (v.Type == DataType.Number)
        {
            try
            {
                return Enum.ToObject(target, (long)v.Number);
            }
            catch
            {
                throw new ScriptRuntimeException($"{key}: 数字超出枚举范围");
            }
        }

        throw new ScriptRuntimeException($"{key}: 需要字符串或数字");
    }

    private static int GetInt(Table t, string key, int fallback)
    {
        var v = t.Get(key);
        return v.Type == DataType.Number ? (int)v.Number : fallback;
    }
}
