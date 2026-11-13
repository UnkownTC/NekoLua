using System.Collections;
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
        typeof(DirectoryInfo),
        typeof(FileInfo),
    ];
    private static readonly ConcurrentDictionary<(Type, BindingFlags), PropertyInfo[]> _propListCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo[]> _staticMethodCache = new();
    private static readonly ConcurrentDictionary<string, string> _camelCache = new();

    /// <summary>
    /// 收集类型的 public static 方法（供 <see cref="ScanStaticMethods"/> 使用）。
    /// 跳过属性访问器、泛型方法定义、仍含泛型参数的方法，
    /// 以及带 <see cref="ObsoleteAttribute"/> 的过时重载（LabAPI 用
    /// <c>[Obsolete(..., error: true)]</c> 标记废弃 API，不应再暴露给脚本）。
    /// </summary>
    private static MethodInfo[] GetStaticMethods(Type type)
    {
        return _staticMethodCache.GetOrAdd(type, static t =>
        {
            var list = new List<MethodInfo>();

            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.IsSpecialName)
                    continue;
                if (m.ContainsGenericParameters)
                    continue;
                if (m.IsGenericMethodDefinition)
                    continue;
                if (m.IsDefined(typeof(ObsoleteAttribute), false))
                    continue;

                list.Add(m);
            }

            return [.. list];
        });
    }

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
    private readonly Dictionary<string, MethodInfo[]> _methods = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ignored = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ignoredMethods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _writeGuards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _callGuards = new(StringComparer.Ordinal);
    private LuaObjectCache? _objectCache;
    private bool _useProxy;
    private HashSet<string>? _onlyMethods;
    private string _prefix = "";

    public LuaTypeMapper UseProxy()
    {
        _useProxy = true;
        return this;
    }

    /// <summary>
    /// 启用对象代理，并复用外部 <see cref="LuaObjectCache"/>，
    /// 保证同一脚本内静态方法与手写集合返回的同一底层对象映射到同一张 Lua 表。
    /// </summary>
    public LuaTypeMapper UseProxy(LuaObjectCache cache)
    {
        _useProxy = true;
        _objectCache = cache;
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

    /// <summary>
    /// 收录 <paramref name="type"/> 的 public static 方法为 Lua 可调用成员
    /// （camelCase 名，自动重载分派，支持 <c>params</c> 与 <c>out</c> 参数）。
    /// 与 <see cref="IgnoreMethods"/> / <see cref="OnlyMethods"/> 配合限定暴露面。
    /// </summary>
    public LuaTypeMapper ScanStaticMethods(Type type)
    {
        foreach (var m in GetStaticMethods(type))
        {
            if (_ignoredMethods.Contains(m.Name))
                continue;
            if (_onlyMethods is not null && !_onlyMethods.Contains(m.Name))
                continue;

            var luaName = ToCamel(m.Name);

            _methods[luaName] = _methods.TryGetValue(luaName, out var existing)
                ? [.. existing, m]
                : [m];
        }
        return this;
    }

    /// <summary>
    /// 黑名单：即使被 <see cref="ScanStaticMethods"/> 收录也不暴露（优先级高于白名单）。
    /// </summary>
    public LuaTypeMapper IgnoreMethods(params string[] csharpNames)
    {
        foreach (var n in csharpNames)
            _ignoredMethods.Add(n);
        return this;
    }

    /// <summary>
    /// 收录 <paramref name="type"/> 的 public static 字段为 Lua **只读**成员。
    /// 用于 <c>Priority</c> 这类「常量类」——编译器把 <c>const</c> 生成为字段而非属性，
    /// <see cref="Scan"/> 看不到它们。
    /// </summary>
    public LuaTypeMapper ScanStaticFields(Type type)
    {
        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (_ignored.Contains(f.Name))
                continue;

            var isSimple = SimpleTypes.Contains(f.FieldType) || f.FieldType.IsEnum;
            if (!isSimple && !_useProxy)
                continue;

            var luaName = ToCamel(f.Name);
            if (_props.ContainsKey(luaName))
                continue;

            var field = f;

            _props[luaName] = new Prop
            {
                Getter = () =>
                {
                    try
                    { return ToDyn(field.GetValue(null)); }
                    catch { return DynValue.Nil; }
                },
                Setter = null,
            };
        }
        return this;
    }

    /// <summary>
    /// 白名单：仅暴露列出的静态方法（与 <see cref="IgnoreMethods"/> 同时生效时后者优先）。
    /// </summary>
    public LuaTypeMapper OnlyMethods(params string[] csharpNames)
    {
        _onlyMethods ??= new HashSet<string>(StringComparer.Ordinal);

        foreach (var n in csharpNames)
            _onlyMethods.Add(n);

        return this;
    }

    /// <summary>
    /// 给指定 Lua 成员名的**写操作**挂 <see cref="LuaGlobalApiGuard"/> 门禁标签。
    /// 用于 <c>Scp914.KnobSetting</c> 这类「一次赋值即改变全服状态」的静态属性。
    /// </summary>
    public LuaTypeMapper GuardWrites(string tag, params string[] luaNames)
    {
        foreach (var n in luaNames)
            _writeGuards[n] = tag;

        return this;
    }

    /// <summary>
    /// 给指定 Lua 方法名的**调用**挂 <see cref="LuaGlobalApiGuard"/> 门禁标签。
    /// </summary>
    public LuaTypeMapper GuardCalls(string tag, params string[] luaNames)
    {
        foreach (var n in luaNames)
            _callGuards[n] = tag;

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

            var name = args[1].String;

            if (_props.TryGetValue(name, out var p))
                return p.Getter();

            if (_methods.TryGetValue(name, out var overloads))
                return DynValue.NewCallback((_, callArgs) => InvokeStatic(name, overloads, callArgs));

            return DynValue.Nil;
        });

        meta["__newindex"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 3 || args[1].Type != DataType.String)
                return DynValue.Nil;

            var name = args[1].String;

            if (!_props.TryGetValue(name, out var p))
                throw new ScriptRuntimeException(
                    _methods.ContainsKey(name)
                        ? $"{_prefix}{name} 是方法，不能被赋值"
                        : $"{_prefix}字段 '{name}' 不存在");

            if (p.Setter is null)
                throw new ScriptRuntimeException($"{_prefix}字段 '{name}' 只读");

            if (_writeGuards.TryGetValue(name, out var tag))
                LuaGlobalApiGuard.EnsureAllowed(tag, $"{_prefix}{name}");

            p.Setter(args[2]);
            return DynValue.Nil;
        });

        t.MetaTable = meta;
        return t;
    }

    /// <summary>
    /// 静态方法重载分派：按「实参个数 + 逐参可转换性」选择第一个可用重载。
    /// 全部候选都无法转换时抛最后一个转换错误（便于脚本定位参数问题）。
    /// </summary>
    private DynValue InvokeStatic(string name, MethodInfo[] overloads, CallbackArguments args)
    {
        if (_callGuards.TryGetValue(name, out var tag))
            LuaGlobalApiGuard.EnsureAllowed(tag, $"{_prefix}{name}");

        ScriptRuntimeException? lastError = null;

        foreach (var m in overloads)
        {
            var ps = m.GetParameters();
            var last = ps.Length - 1;
            var hasParams = ps.Length > 0
                            && ps[last].IsDefined(typeof(ParamArrayAttribute), false);
            var fixedCount = hasParams ? ps.Length - 1 : ps.Length;
            var required = 0;

            for (var i = 0; i < ps.Length; i++)
            {
                if (ps[i].IsOptional || ps[i].IsOut)
                    continue;
                if (hasParams && i == fixedCount)
                    continue;
                required++;
            }

            if (args.Count < required)
                continue;
            if (!hasParams && args.Count > ps.Length)
                continue;

            var callArgs = new object?[ps.Length];
            var outIndexes = new List<int>();

            try
            {
                for (var i = 0; i < ps.Length; i++)
                {
                    if (ps[i].IsOut)
                    {
                        var elem = ps[i].ParameterType.GetElementType()!;
                        callArgs[i] = elem.IsValueType ? Activator.CreateInstance(elem) : null;
                        outIndexes.Add(i);
                        continue;
                    }

                    if (hasParams && i == fixedCount)
                    {
                        var elem = ps[i].ParameterType.GetElementType()!;
                        var extra = args.Count - fixedCount;
                        var arr = Array.CreateInstance(elem, extra);

                        for (var k = 0; k < extra; k++)
                        {
                            arr.SetValue(
                                FromDyn(args[fixedCount + k], elem, ArgKey(name, ps[i])),
                                k);
                        }

                        callArgs[i] = arr;
                        continue;
                    }

                    if (i < args.Count)
                    {
                        callArgs[i] = FromDyn(args[i], ps[i].ParameterType, ArgKey(name, ps[i]));
                        continue;
                    }

                    callArgs[i] = ps[i].HasDefaultValue
                        ? ps[i].DefaultValue
                        : ps[i].ParameterType.IsValueType
                            ? Activator.CreateInstance(ps[i].ParameterType)
                            : null;
                }
            }
            catch (ScriptRuntimeException ex)
            {
                lastError = ex;
                continue;
            }

            try
            {
                var result = m.Invoke(null, callArgs);

                if (outIndexes.Count == 0)
                    return ToDyn(result);

                var all = new DynValue[outIndexes.Count + 1];
                all[0] = ToDyn(result);

                for (var k = 0; k < outIndexes.Count; k++)
                    all[k + 1] = ToDyn(callArgs[outIndexes[k]]);

                return DynValue.NewTuple(all);
            }
            catch (TargetInvocationException ex)
            {
                throw new ScriptRuntimeException(
                    $"{_prefix}{name}: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (ScriptRuntimeException) { throw; }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"{_prefix}{name}: {ex.Message}");
            }
        }

        throw lastError ?? new ScriptRuntimeException(
            $"{_prefix}{name}: 参数不匹配（{args.Count} 个实参无可用重载）");
    }

    private string ArgKey(string method, ParameterInfo p)
        => $"{_prefix}{method}({p.Name ?? "参数"})";

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

        // LabAPI PathManager / AssemblyUtils 等返回的 IO 类型：只暴露绝对路径字符串（信息），
        // 不赋予读写能力（读写仍受 LuaPathGuard 的 mod 作用域白名单约束）。
        DirectoryInfo d => DynValue.NewString(d.FullName),
        FileInfo f => DynValue.NewString(f.FullName),

        // 数组按集合语义打包（如 Announcer.AllLines / CassieLine[]），避免退化成不透明代理对象。
        Array a => PackArray(a),

        // LabAPI 的静态集合属性（Player.List / Map.Rooms / Room.List …）与
        // 各类缓存字典。字典先于集合判断（IDictionary 也实现 IEnumerable）。
        IDictionary d => PackDictionary(d),
        IEnumerable e => PackEnumerable(e),

        // 其余结构体（UnityEngine.Pose / RaycastHit / LayerMask …）按字段表打包，
        // 避免退化成不透明的反射代理对象。
        object o when o.GetType().IsValueType => PackStruct(o),

        _ => _useProxy && value is not null
            ? ProxyCache.Wrap(value)
            : DynValue.Nil,
    };

    private DynValue PackStruct(object value)
    {
        var type = value.GetType();
        var t = new Table(_script);

        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || !p.CanRead)
                continue;

            try
            { t[p.Name] = ToDyn(p.GetValue(value)); }
            catch (Exception ex)
            {
                LuaDiagnostics.WarnOnce($"对象序列化: 读取属性 {type.Name}.{p.Name} 失败，该成员已跳过", ex);
            }
        }

        foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            try
            { t[f.Name] = ToDyn(f.GetValue(value)); }
            catch (Exception ex)
            {
                LuaDiagnostics.WarnOnce($"对象序列化: 读取字段 {type.Name}.{f.Name} 失败，该成员已跳过", ex);
            }
        }

        return DynValue.NewTable(t);
    }

    private const int MaxCollectionSize = 512;

    private DynValue PackArray(Array array)
    {
        var t = new Table(_script);
        var i = 1;

        foreach (var item in array)
        {
            if (i > MaxCollectionSize)
                break;

            t[i++] = ToDyn(item);
        }

        return DynValue.NewTable(t);
    }

    private DynValue PackEnumerable(IEnumerable source)
    {
        var t = new Table(_script);
        var i = 1;

        foreach (var item in source)
        {
            if (i > MaxCollectionSize)
                break;

            t[i++] = ToDyn(item);
        }

        return DynValue.NewTable(t);
    }

    /// <summary>
    /// 字典打包为 Lua 表。键必须能转成字符串或数字；引用类型键（ReferenceHub、
    /// RoomIdentifier 等代理表）没有稳定的 Lua 等价物，直接跳过，
    /// 用值的数组形态（<see cref="PackEnumerable"/>）取数据更可靠。
    /// </summary>
    private DynValue PackDictionary(IDictionary dict)
    {
        var t = new Table(_script);
        var n = 0;

        foreach (DictionaryEntry entry in dict)
        {
            if (n > MaxCollectionSize)
                break;

            var key = ToDyn(entry.Key);

            if (key.Type != DataType.String && key.Type != DataType.Number)
                continue;

            t[key] = ToDyn(entry.Value);
            n++;
        }

        return DynValue.NewTable(t);
    }

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
            return DateTimeOffset.FromUnixTimeMilliseconds(
                LuaNumbers.ToUnixMilliseconds(v.Number, $"{key}（Unix 秒）"))
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

        if (target.IsArray && v.Type == DataType.Table)
        {
            var elem = target.GetElementType()!;
            var tbl = v.Table;
            var n = tbl.Length;

            var arr = Array.CreateInstance(elem, n);
            for (var i = 1; i <= n; i++)
                arr.SetValue(FromDyn(tbl.Get(i), elem, key), i - 1);
            return arr;
        }

        // LabAPI 有大量 IEnumerable<T> / IReadOnlyCollection<T> 形参
        // （如 Server.SendAdminChatMessage(IEnumerable<Player>, …)），统一按数组语义转换。
        var collectionElem = GetCollectionElementType(target);

        if (collectionElem is not null && v.Type == DataType.Table)
        {
            var tbl = v.Table;
            var n = tbl.Length;
            var list = (IList)Activator.CreateInstance(
                typeof(List<>).MakeGenericType(collectionElem))!;

            for (var i = 1; i <= n; i++)
                list.Add(FromDyn(tbl.Get(i), collectionElem, key));

            return list;
        }

        // 代理表 → 原始 C# 对象（静态方法/事件回调里把 player、door、room 等
        // 由 LuaObjectProxy 包装的对象再传回 LabAPI 时使用）。
        if (v.Type == DataType.Table && !target.IsValueType)
        {
            var unwrapped = LuaObjectProxy.Unwrap(v.Table);

            if (unwrapped is not null && target.IsAssignableFrom(unwrapped.GetType()))
                return unwrapped;
        }

        if (v.Type == DataType.Nil)
            return target.IsValueType ? Activator.CreateInstance(target) : null;

        if (v.Type != DataType.Number)
            throw new ScriptRuntimeException($"{key}: 需要数字");

        var d = v.Number;
        // 审查 P3：整数目标类型走范围校验，避免越界静默回绕
        var converted = LuaNumbers.TryConvert(Type.GetTypeCode(target), d, key);
        if (converted is not null)
            return converted;

        return Type.GetTypeCode(target) switch
        {
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

    /// <summary>
    /// 若 <paramref name="type"/> 是可枚举的泛型集合（<c>IEnumerable&lt;T&gt;</c> /
    /// <c>IReadOnlyCollection&lt;T&gt;</c> / <c>ICollection&lt;T&gt;</c> / <c>IList&lt;T&gt;</c> /
    /// <c>List&lt;T&gt;</c> / <c>HashSet&lt;T&gt;</c>），返回其元素类型；否则返回 <c>null</c>。
    /// <c>string</c> 与数组单独处理，不走此路径。
    /// </summary>
    private static Type? GetCollectionElementType(Type type)
    {
        if (type == typeof(string) || type.IsArray || !type.IsGenericType)
            return null;

        var def = type.GetGenericTypeDefinition();

        if (def != typeof(IEnumerable<>)
            && def != typeof(IReadOnlyCollection<>)
            && def != typeof(IReadOnlyList<>)
            && def != typeof(ICollection<>)
            && def != typeof(IList<>)
            && def != typeof(List<>)
            && def != typeof(HashSet<>)
            && def != typeof(ISet<>))
        {
            return null;
        }

        return type.GetGenericArguments()[0];
    }
}
