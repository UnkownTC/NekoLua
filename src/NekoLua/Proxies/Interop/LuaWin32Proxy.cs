using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Interop;

/// <summary>
/// win32 表（由 Config.EnableWin32 控制，默认关闭）：
///
///   win32.triger(dll, func, args) -> number
///     直接调用 Win32 函数
///     args：数组 {a, b, c} 或带类型标注的 table
///            {value = n, type = "int32"|"int64"|"uint"|"double"|"ptr"}
///     返回 int32（BOOL/HRESULT 等语义由调用方决定）
///
///   win32.bind(dll, func, aliasName [, callback])
///     绑定 native 函数为全局名字：
///       win32.bind("kernel32.dll", "Beep", "MyBeep")
///       MyBeep(1000, 1000)
///
///     若提供 callback，其**引用**若能在 Globals 中找到对应变量，
///     该变量会被替换为转发函数（兼容用户示例）
///
///   win32.unbind(aliasName) -> bool
///     移除绑定
///
///   win32.ptr(value) -> ptr
///     从 int64 构造 IntPtr 封装
///
///   win32.loaded() -> table     当前加载的 dll 列表
///   win32.funcs()  -> table     当前缓存的函数名列表
///   win32.free(dll)             释放 dll 引用
///
/// 白名单：Config.Win32DllWhitelist / Win32FunctionWhitelist
/// </summary>
public class LuaWin32Proxy : IRegisterLua
{
    public string Name { get; } = "win32";
    private static readonly Dictionary<string, (string Dll, string Func)> _bindings = [];
    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var t = new Table(script);


        var trigger = DynValue.NewCallback((_, args) => Trigger(mod, args));
        t["triger"] = trigger;
        t["trigger"] = trigger;


        t["bind"] = DynValue.NewCallback((_, args) =>
        {
            CheckEnabled();

            if (args.Count < 3 ||
                args[0].Type != DataType.String ||
                args[1].Type != DataType.String ||
                args[2].Type != DataType.String)
                throw new ScriptRuntimeException(
                    "win32.bind(dll, func, aliasName [, callback])");

            var dll = args[0].String;
            var func = args[1].String;
            var alias = args[2].String;

            CheckWhitelist(dll, func);

            _bindings[alias] = (dll, func);

            var forwarder = DynValue.NewCallback((_, callArgs) =>
                LuaWin32Native.Call(script, dll, func, callArgs));

            script.Globals[alias] = forwarder;

            if (args.Count > 3 && args[3].Type == DataType.Function)
            {
                var target = args[3];
                var keysToReplace = new List<string>();

                foreach (var p in script.Globals.Pairs)
                {
                    if (p.Key.Type != DataType.String)
                        continue;
                    if (p.Value.Type != DataType.Function)
                        continue;
                    if (p.Value.Function != target.Function)
                        continue;

                    keysToReplace.Add(p.Key.String);
                }

                foreach (var k in keysToReplace)
                    script.Globals[k] = forwarder;
            }

            return DynValue.Nil;
        });

        t["unbind"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("win32.unbind(aliasName)");

            var alias = args[0].String;
            var removed = _bindings.Remove(alias);

            script.Globals[alias] = DynValue.Nil;

            return DynValue.NewBoolean(removed);
        });

        t["ptr"] = DynValue.NewCallback((_, args) =>
        {
            return args.Count < 1 || args[0].Type != DataType.Number
                ? throw new ScriptRuntimeException("win32.ptr(value)")
                : DynValue.NewTable(
                LuaIntPtrProxy.Build(script, new IntPtr((long)args[0].Number)));
        });

        t["loaded"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(script);
            var i = 1;
            foreach (var k in _bindings.Keys)
            {
                var entry = new Table(script) { ["alias"] = k };
                entry["dll"] = _bindings[k].Dll;
                entry["func"] = _bindings[k].Func;
                arr[i++] = DynValue.NewTable(entry);
            }
            return DynValue.NewTable(arr);
        });

        t["bindings"] = t["loaded"];

        t["free"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("win32.free(dll)");

            LuaWin32Native.UnloadModule(args[0].String);
            return DynValue.Nil;
        });

        return t;
    }

    // ================================================================

    private static DynValue Trigger(LuaModInstance mod, CallbackArguments args)
    {
        CheckEnabled();

        if (args.Count < 3 ||
            args[0].Type != DataType.String ||
            args[1].Type != DataType.String ||
            args[2].Type != DataType.Table)
            throw new ScriptRuntimeException(
                "win32.triger(dll, func, args)");

        var dll = args[0].String;
        var func = args[1].String;
        var argTable = args[2].Table;

        CheckWhitelist(dll, func);

        var nativeArgs = new List<DynValue>();

        if (IsArray(argTable))
        {
            for (var i = 1; i <= argTable.Length; i++)
                nativeArgs.Add(argTable.Get(i));
        }
        else
        {
            var entries = new List<(string Key, DynValue Value)>();
            foreach (var p in argTable.Pairs)
            {
                if (p.Key.Type != DataType.String)
                    continue;
                entries.Add((p.Key.String, p.Value));
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            foreach (var (_, v) in entries)
                nativeArgs.Add(v);
        }

        var inner = new CallbackArguments(nativeArgs, isMethodCall: false);
        return LuaWin32Native.Call(mod.Script, dll, func, inner);
    }

    private static bool IsArray(Table t)
    {
        var count = 0;
        var max = 0;
        foreach (var p in t.Pairs)
        {
            if (p.Key.Type != DataType.Number)
                return false;
            var n = p.Key.Number;
            if (n < 1 || n != Math.Floor(n))
                return false;
            if (n > max)
                max = (int)n;
            count++;
        }
        return count > 0 && count == max;
    }
    private static void CheckEnabled()
    {
        if (!MainPlugin.CacheConfig.EnableWin32)
            throw new ScriptRuntimeException(
                "win32: 未启用（Config.EnableWin32 = false）");
    }
    private static void CheckWhitelist(string dll, string func)
    {
        var cfg = MainPlugin.CacheConfig;

        // 审查 P2-10：函数黑名单优先级最高——即使白名单为空也生效，
        // 防止 kernel32/ntdll/advapi32 默认放行等价暴露进程创建与任意内存读写
        if (cfg.Win32FunctionBlacklist is { Length: > 0 })
        {
            foreach (var b in cfg.Win32FunctionBlacklist)
            {
                if (!string.IsNullOrWhiteSpace(b) &&
                    string.Equals(b, func, StringComparison.OrdinalIgnoreCase))
                    throw new ScriptRuntimeException(
                        $"win32: 函数 '{func}' 在禁止清单中（Config.Win32FunctionBlacklist）");
            }
        }

        if (cfg.Win32DllWhitelist is { Length: > 0 })
        {
            var ok = false;
            foreach (var w in cfg.Win32DllWhitelist)
                if (string.Equals(w, dll, StringComparison.OrdinalIgnoreCase))
                { ok = true; break; }
            if (!ok)
                throw new ScriptRuntimeException($"win32: dll '{dll}' 不在白名单中");
        }

        if (cfg.Win32FunctionWhitelist is { Length: > 0 })
        {
            var ok = false;
            foreach (var w in cfg.Win32FunctionWhitelist)
                if (string.Equals(w, func, StringComparison.OrdinalIgnoreCase))
                { ok = true; break; }
            if (!ok)
                throw new ScriptRuntimeException($"win32: 函数 '{func}' 不在白名单中");
        }
    }
}
