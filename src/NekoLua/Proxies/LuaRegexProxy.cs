using System.Text.RegularExpressions;

using MoonSharp.Interpreter;

using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies;

/// <summary>
/// regex 表（.NET 正则，区别于 Lua 的模式匹配）：
///
///   regex.isMatch(str, pattern)         -> bool
///   regex.match(str, pattern)           -> table | nil
///     {value, index(1-based), length, groups}
///     groups 支持数字索引（含 0=整体）和命名组
///   regex.matchAll(str, pattern)        -> table   全部匹配
///   regex.findAll(str, pattern)         -> table   只返回匹配字符串
///   regex.replace(str, pattern, repl)   -> string  支持 $1 / ${name}
///   regex.split(str, pattern)           -> table
///   regex.escape(str)                   -> string  转义正则特殊字符
///   regex.isValid(pattern)              -> bool
///   regex.cacheCount()                  -> number  当前 mod 缓存的正则数
///
/// 每个 mod 独立 LRU 缓存（上限 256），Reload / Disable 时自动清理。
/// </summary>
public class LuaRegexProxy : IRegisterLua
{
    public string Name { get; } = "regex";
    private const int MaxCacheSize = 256;

    // 无超时的正则一旦回溯爆炸会无限占用主线程（玩家输入等不可信数据可间接触发）；
    // 1s 上限足够覆盖正常用例，同时保证冻结可恢复（审查 P2-7）
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(1000);
    private const int MaxMatches = 10000;
    private sealed class RegexCache
    {
        private readonly Dictionary<string, Regex> _map = new(StringComparer.Ordinal);
        private readonly List<string> _order = [];

        public Regex Get(string pattern)
        {
            if (_map.TryGetValue(pattern, out var re))
                return re;

            if (_order.Count >= MaxCacheSize)
            {
                var oldest = _order[0];
                _order.RemoveAt(0);
                _map.Remove(oldest);
            }

            re = new Regex(pattern, RegexOptions.Compiled, MatchTimeout);
            _map[pattern] = re;
            _order.Add(pattern);
            return re;
        }

        public int Count => _map.Count;
        public void Clear() { _map.Clear(); _order.Clear(); }
    }
    private static readonly Dictionary<LuaModInstance, RegexCache> _caches = [];
    private static RegexCache CacheOf(LuaModInstance mod)
    {
        if (!_caches.TryGetValue(mod, out var c))
            _caches[mod] = c = new RegexCache();
        return c;
    }
    public static int CountOf(LuaModInstance mod)
        => _caches.TryGetValue(mod, out var c) ? c.Count : 0;
    public static void CleanupFor(LuaModInstance mod) => _caches.Remove(mod);
    public static void ClearAll() => _caches.Clear();
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);
        var cache = CacheOf(mod);

        t["isMatch"] = DynValue.NewCallback((_, args) =>
        {
            var (str, re) = Parse(cache, args, "regex.isMatch(str, pattern)");
            return DynValue.NewBoolean(Guarded("regex.isMatch", () => re.IsMatch(str)));
        });

        t["match"] = DynValue.NewCallback((_, args) =>
        {
            var (str, re) = Parse(cache, args, "regex.match(str, pattern)");
            var m = Guarded("regex.match", () => re.Match(str));
            return m.Success ? ToLuaMatch(s, re, m) : DynValue.Nil;
        });

        t["matchAll"] = DynValue.NewCallback((_, args) =>
        {
            var (str, re) = Parse(cache, args, "regex.matchAll(str, pattern)");
            var tbl = new Table(s);
            var i = 1;
            foreach (Match m in Guarded("regex.matchAll", () => re.Matches(str)))
            {
                // 零长匹配模式会对长输入产生等长数量的匹配，必须封顶（审查 P2-7）
                if (i > MaxMatches)
                    throw new ScriptRuntimeException($"regex.matchAll: 匹配数超过 {MaxMatches} 上限");
                tbl[i++] = ToLuaMatch(s, re, m);
            }
            return DynValue.NewTable(tbl);
        });

        t["findAll"] = DynValue.NewCallback((_, args) =>
        {
            var (str, re) = Parse(cache, args, "regex.findAll(str, pattern)");
            var tbl = new Table(s);
            var i = 1;
            foreach (Match m in Guarded("regex.findAll", () => re.Matches(str)))
            {
                if (i > MaxMatches)
                    throw new ScriptRuntimeException($"regex.findAll: 匹配数超过 {MaxMatches} 上限");
                tbl[i++] = DynValue.NewString(m.Value);
            }
            return DynValue.NewTable(tbl);
        });

        t["replace"] = DynValue.NewCallback((_, args) =>
        {
            var (str, re) = Parse(cache, args, "regex.replace(str, pattern, replacement)");
            if (args.Count < 3 || args[2].Type != DataType.String)
                throw new ScriptRuntimeException("regex.replace(str, pattern, replacement)");

            return DynValue.NewString(Guarded("regex.replace", () => re.Replace(str, args[2].String)));
        });

        t["split"] = DynValue.NewCallback((_, args) =>
        {
            var (str, re) = Parse(cache, args, "regex.split(str, pattern)");
            var tbl = new Table(s);
            var i = 1;
            foreach (var part in Guarded("regex.split", () => re.Split(str)))
                tbl[i++] = DynValue.NewString(part);
            return DynValue.NewTable(tbl);
        });

        t["escape"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("regex.escape(str)");

            return DynValue.NewString(Regex.Escape(args[0].String));
        });

        t["isValid"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                return DynValue.False;

            try
            {
                cache.Get(args[0].String);
                return DynValue.True;
            }
            catch
            {
                return DynValue.False;
            }
        });

        t["cacheCount"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(cache.Count));

        return t;
    }
    static (string str, Regex re) Parse(RegexCache cache, CallbackArguments args, string sig)
    {
        if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.String)
            throw new ScriptRuntimeException(sig);

        try
        {
            return (args[0].String, cache.Get(args[1].String));
        }
        catch (ArgumentException ex)
        {
            throw new ScriptRuntimeException($"{sig}: 非法正则: {ex.Message}");
        }
    }

    // 匹配超时（回溯爆炸被 MatchTimeout 打断）必须包装成脚本异常，
    // 否则 RegexMatchTimeoutException 会裸穿到宿主边界（审查 P2-7）
    static T Guarded<T>(string sig, Func<T> f)
    {
        try
        {
            return f();
        }
        catch (RegexMatchTimeoutException)
        {
            throw new ScriptRuntimeException($"{sig}: 正则匹配超过 {MatchTimeout.TotalSeconds:0.#}s 超时上限（疑似灾难性回溯），已中止");
        }
    }

    static DynValue ToLuaMatch(Script s, Regex re, Match m)
    {
        var groups = new Table(s);

        for (var i = 0; i < m.Groups.Count; i++)
        {
            var g = m.Groups[i];
            groups[i] = DynValue.NewString(g.Success ? g.Value : "");
        }

        foreach (var name in re.GetGroupNames())
        {
            if (int.TryParse(name, out _))
                continue;
            var g = m.Groups[name];
            groups[name] = DynValue.NewString(g.Success ? g.Value : "");
        }

        var t = new Table(s)
        {
            ["value"] = m.Value,
            ["index"] = m.Index + 1,
            ["length"] = m.Length,
            ["groups"] = DynValue.NewTable(groups),
        };
        return DynValue.NewTable(t);
    }
}
