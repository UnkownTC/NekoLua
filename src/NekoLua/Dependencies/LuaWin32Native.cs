using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

using MoonSharp.Interpreter;

using NekoLua.Proxies.Interop;

namespace NekoLua.Dependencies;

public static class LuaWin32Native
{
    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);
    private sealed class ModuleEntry
    {
        public IntPtr Handle;
        public int RefCount;
    }
    private static readonly Dictionary<string, ModuleEntry> _modules =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, IntPtr> _functions =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Delegate> _stubs = [];
    private static readonly object _lock = new();
    public static IntPtr LoadModule(string dllName)
    {
        lock (_lock)
        {
            if (_modules.TryGetValue(dllName, out var m))
            {
                m.RefCount++;
                return m.Handle;
            }

            var h = LoadLibrary(dllName);
            if (h == IntPtr.Zero)
                throw new ScriptRuntimeException(
                    $"win32: 加载 '{dllName}' 失败 (GetLastError={Marshal.GetLastWin32Error()})");

            _modules[dllName] = new ModuleEntry { Handle = h, RefCount = 1 };
            return h;
        }
    }

    public static void UnloadModule(string dllName)
    {
        lock (_lock)
        {
            if (!_modules.TryGetValue(dllName, out var m))
                return;
            if (--m.RefCount > 0)
                return;

            // 归零卸载时必须同步清掉该模块的函数指针与调用桩缓存：
            // 否则下次 Resolve 命中已卸载模块的悬空地址，calli 直接 AV 崩进程（审查 P2-11）
            var modulePrefix = dllName + "!";
            var staleFuncKeys = new List<string>();
            var stalePtrs = new List<string>();

            foreach (var kv in _functions)
            {
                if (kv.Key.StartsWith(modulePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    staleFuncKeys.Add(kv.Key);
                    stalePtrs.Add(kv.Value.ToString());
                }
            }

            var staleStubKeys = new List<string>(
                from stubKey in _stubs.Keys
                from ptr in stalePtrs
                where stubKey.StartsWith(ptr + "|", StringComparison.Ordinal)
                select stubKey);

            foreach (var key in staleFuncKeys)
                _ = _functions.Remove(key);
            foreach (var key in staleStubKeys)
                _ = _stubs.Remove(key);

            FreeLibrary(m.Handle);
            _modules.Remove(dllName);
        }
    }

    public static IntPtr Resolve(string dllName, string funcName)
    {
        var key = $"{dllName}!{funcName}";
        lock (_lock)
        {
            if (_functions.TryGetValue(key, out var cached))
                return cached;
        }

        var h = LoadModule(dllName);
        var p = GetProcAddress(h, funcName);
        if (p == IntPtr.Zero)
            throw new ScriptRuntimeException(
                $"win32: 在 '{dllName}' 中找不到 '{funcName}'");

        lock (_lock)
            _functions[key] = p;
        return p;
    }
    public static DynValue Call(Script script, string dllName, string funcName, CallbackArguments args)
    {
        var funcPtr = Resolve(dllName, funcName);

        var luaArgs = new List<DynValue>();
        for (var i = 0; i < args.Count; i++)
            luaArgs.Add(args[i]);

        var paramTypes = new Type[luaArgs.Count];
        var nativeArgs = new object?[luaArgs.Count];

        for (var i = 0; i < luaArgs.Count; i++)
        {
            var (t, v) = MapLuaToNative(luaArgs[i]);
            paramTypes[i] = t;
            nativeArgs[i] = v;
        }

        var stubKey = $"{funcPtr}|{string.Join(",", paramTypes.Select(x => x.Name))}";

        Delegate del;
        lock (_lock)
        {
            if (!_stubs.TryGetValue(stubKey, out var cached))
            {
                cached = BuildStub(funcPtr, paramTypes);
                _stubs[stubKey] = cached;
            }
            del = cached;
        }

        object?[] allArgs = new object?[nativeArgs.Length + 1];
        allArgs[0] = funcPtr;
        Array.Copy(nativeArgs, 0, allArgs, 1, nativeArgs.Length);

        object? result;
        try
        {
            result = del.DynamicInvoke(allArgs);
        }
        catch (Exception ex)
        {
            throw new ScriptRuntimeException($"win32: 调用 '{funcName}' 失败: {ex.Message}");
        }

        return PackResult(script, result);
    }
    private static DynValue PackResult(Script script, object? result)
    {
        return result switch
        {
            null => DynValue.Nil,
            int i => DynValue.NewNumber(i),
            uint u => DynValue.NewNumber(u),
            long l => DynValue.NewNumber(l),
            ulong u => DynValue.NewNumber(u),
            short sh => DynValue.NewNumber(sh),
            ushort us => DynValue.NewNumber(us),
            byte b => DynValue.NewNumber(b),
            sbyte sb => DynValue.NewNumber(sb),
            bool bo => DynValue.NewBoolean(bo),
            IntPtr ip => MainPlugin.CacheConfig.Win32IntPtrAsTable
                                ? DynValue.NewTable(LuaIntPtrProxy.Build(script, ip))
                                : DynValue.NewNumber(ip.ToInt64()),
            string str => DynValue.NewString(str),
            _ => DynValue.NewNumber(0),
        };
    }
    private static Delegate BuildStub(IntPtr _, Type[] paramTypes)
    {
        var dm = new DynamicMethod(
            "Win32Call",
            typeof(int),
            [typeof(IntPtr), .. paramTypes],
            typeof(LuaWin32Native).Module,
            skipVisibility: true);

        var il = dm.GetILGenerator();

        for (var i = 0; i < paramTypes.Length + 1; i++)
            il.Emit(OpCodes.Ldarg, i);

        il.EmitCalli(
            OpCodes.Calli,
            CallingConventions.Standard,
            typeof(int),
            paramTypes,
            null);

        il.Emit(OpCodes.Ret);

        var types = new[] { typeof(IntPtr) }
            .Concat(paramTypes)
            .Append(typeof(int))
            .ToArray();

        var delType = Expression.GetDelegateType(types);
        return dm.CreateDelegate(delType);
    }
    private static (Type, object?) MapLuaToNative(DynValue v)
    {
        switch (v.Type)
        {
            case DataType.Number:
                {
                    var d = v.Number;
                    if (d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue)
                        return (typeof(int), (int)d);
                    if (d == Math.Floor(d))
                        return (typeof(long), (long)d);
                    return (typeof(double), d);
                }

            case DataType.String:
                return (typeof(string), v.String);

            case DataType.Boolean:
                return (typeof(int), v.Boolean ? 1 : 0);

            case DataType.Table:
                {
                    if (!MainPlugin.CacheConfig.Win32AllowPointerArgs &&
                        IsPointerArgTable(v.Table))
                        throw new ScriptRuntimeException(
                            "win32: 禁止传入指针实参（Config.Win32AllowPointerArgs = false）");

                    // 支持 intptr 代理表（含 value 字段）
                    var val = v.Table.Get("value");
                    if (val.Type == DataType.Number)
                    {
                        var type = v.Table.Get("type");
                        if (type.Type == DataType.String)
                        {
                            return type.String.ToLowerInvariant() switch
                            {
                                "int32" or "int" => (typeof(int), LuaNumbers.ToInt32(val.Number, "win32: value(int32)")),
                                "int64" or "long" => (typeof(long), LuaNumbers.ToInt64(val.Number, "win32: value(int64)")),
                                "uint" => (typeof(uint), LuaNumbers.ToUInt32(val.Number, "win32: value(uint)")),
                                "uint64" or "ulong" => (typeof(ulong), LuaNumbers.ToUInt64(val.Number, "win32: value(uint64)")),
                                "double" => (typeof(double), val.Number),
                                "ptr" or "intptr" => (typeof(IntPtr), new IntPtr(LuaNumbers.ToInt64(val.Number, "win32: value(intptr)"))),
                                _ => (typeof(int), LuaNumbers.ToInt32(val.Number, "win32: value(int32)")),
                            };
                        }

                        // 无 type 标注：默认把 value 当 int32
                        return (typeof(int), LuaNumbers.ToInt32(val.Number, "win32: value(int32)"));
                    }


                    var ptr = v.Table.Get("ptr");
                    if (ptr.Type == DataType.Number)
                        return (typeof(IntPtr), new IntPtr((long)ptr.Number));

                    break;
                }
        }

        return (typeof(int), 0);
    }

    /// <summary>
    /// 判断实参 table 是否表达「指针」（显式 <c>type="ptr"</c> 或 <c>{ptr = n}</c> 形式）。
    /// 审查 P2-10：默认拒绝，防止脚本把任意整数当地址交给原生函数。
    /// </summary>
    private static bool IsPointerArgTable(Table t)
    {
        var type = t.Get("type");

        if (type.Type == DataType.String)
        {
            var s = type.String;

            if (string.Equals(s, "ptr", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s, "intptr", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return t.Get("ptr").Type == DataType.Number;
    }

    public static void ClearAll()
    {
        lock (_lock)
        {
            _stubs.Clear();
            _functions.Clear();
            foreach (var m in _modules.Values)
                try
                { FreeLibrary(m.Handle); }
                catch (Exception ex)
                {
                    LuaDiagnostics.WarnOnce($"win32: FreeLibrary 失败（handle={m.Handle}），句柄可能已回收", ex);
                }
            _modules.Clear();
        }
    }
}
