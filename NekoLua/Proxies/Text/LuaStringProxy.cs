using System.Text;
using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Text;

/// <summary>
/// 扩展 Lua 原生 string 表（不替换）：
///   string.split / lines / trim / ltrim / rtrim
///   startswith / endswith / contains / isBlank / isEmpty
///   replace / replaceFirst
///   capitalize / title
///   padStart / padEnd / center / truncate
///   reverse / count / indexOf / lastIndexOf
///   humanize / formatBytes
///   alnumOnly / unquote
///
/// 原生 string.*（format / sub / gsub / find / rep / byte / char / match / gmatch）不受影响。
/// </summary>
public class LuaStringProxy : IRegisterLua
{
    public string Name { get; } = "string";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = s.Globals.Get("string").Table;

        t["split"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.split(s, sep)");
            var sep = args.Count > 1 && args[1].Type == DataType.String
                ? args[1].String : ",";

            if (sep.Length == 0)
            {
                var one = new Table(s);
                one[1] = src;
                return DynValue.NewTable(one);
            }

            var parts = src.Split([sep], StringSplitOptions.None);
            return PackStringArray(s, parts);
        });

        t["lines"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.lines(s)");
            var parts = src.Replace("\r\n", "\n").Split('\n');
            return PackStringArray(s, parts);
        });

        t["trim"] = UnaryString(s0 => s0.Trim(), "string.trim(s)");
        t["ltrim"] = UnaryString(s0 => s0.TrimStart(), "string.ltrim(s)");
        t["rtrim"] = UnaryString(s0 => s0.TrimEnd(), "string.rtrim(s)");

        t["startswith"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.startswith(s, prefix)");
            var prefix = ArgString(args, 1, "string.startswith(s, prefix)");
            return DynValue.NewBoolean(src.StartsWith(prefix, StringComparison.Ordinal));
        });

        t["endswith"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.endswith(s, suffix)");
            var suffix = ArgString(args, 1, "string.endswith(s, suffix)");
            return DynValue.NewBoolean(src.EndsWith(suffix, StringComparison.Ordinal));
        });

        t["contains"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.contains(s, needle)");
            var needle = ArgString(args, 1, "string.contains(s, needle)");
            return DynValue.NewBoolean(src.Contains(needle, StringComparison.Ordinal));
        });

        t["isBlank"] = DynValue.NewCallback((_, args) =>
        {
            var v = args.Count > 0 ? args[0] : DynValue.Nil;
            if (v.Type is DataType.Nil or DataType.Void) return DynValue.True;
            return v.Type != DataType.String ? DynValue.False : DynValue.NewBoolean(string.IsNullOrWhiteSpace(v.String));
        });

        t["isEmpty"] = DynValue.NewCallback((_, args) =>
        {
            var v = args.Count > 0 ? args[0] : DynValue.Nil;
            if (v.Type is DataType.Nil or DataType.Void) return DynValue.True;
            return v.Type != DataType.String ? DynValue.False : DynValue.NewBoolean(v.String.Length == 0);
        });

        t["replace"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.replace(s, needle, rep)");
            var oldV = ArgString(args, 1, "string.replace(s, needle, rep)");
            var newV = args.Count > 2 && args[2].Type == DataType.String
                ? args[2].String : "";

            return oldV.Length == 0 ? DynValue.NewString(src) : DynValue.NewString(src.Replace(oldV, newV));
        });

        t["replaceFirst"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.replaceFirst(s, needle, rep[, n])");
            var oldV = ArgString(args, 1, "string.replaceFirst(s, needle, rep[, n])");
            var newV = args.Count > 2 && args[2].Type == DataType.String
                ? args[2].String : "";
            var n = args.Count > 3 && args[3].Type == DataType.Number
                ? (int)args[3].Number : 1;

            if (oldV.Length == 0 || n < 1) return DynValue.NewString(src);

            var sb = new StringBuilder(src.Length);
            var idx = 0;
            var replaced = 0;

            while (replaced < n)
            {
                var found = src.IndexOf(oldV, idx, StringComparison.Ordinal);
                if (found < 0)
                {
                    sb.Append(src, idx, src.Length - idx);
                    break;
                }
                sb.Append(src, idx, found - idx);
                sb.Append(newV);
                idx = found + oldV.Length;
                replaced++;
            }

            if (idx < src.Length) sb.Append(src, idx, src.Length - idx);
            return DynValue.NewString(sb.ToString());
        });

        t["capitalize"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.capitalize(s)");
            return src.Length == 0 ? DynValue.NewString(src) : DynValue.NewString(char.ToUpperInvariant(src[0]) + src.Substring(1));
        });

        t["title"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.title(s)");
            var sb = new StringBuilder(src.Length);
            var newWord = true;

            foreach (var c in src)
            {
                if (char.IsLetter(c))
                {
                    sb.Append(newWord ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                    newWord = false;
                }
                else
                {
                    sb.Append(c);
                    newWord = true;
                }
            }

            return DynValue.NewString(sb.ToString());
        });

        t["padStart"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.padStart(s, len[, fill])");
            var len = ArgInt(args, 1, 0, "string.padStart(s, len[, fill])");
            var fill = args.Count > 2 && args[2].Type == DataType.String
                ? args[2].String : " ";

            return src.Length >= len || fill.Length == 0 ? DynValue.NewString(src) : DynValue.NewString(src.PadLeft(len, fill[0]));
        });

        t["padEnd"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.padEnd(s, len[, fill])");
            var len = ArgInt(args, 1, 0, "string.padEnd(s, len[, fill])");
            var fill = args.Count > 2 && args[2].Type == DataType.String
                ? args[2].String : " ";

            return src.Length >= len || fill.Length == 0 ? DynValue.NewString(src) : DynValue.NewString(src.PadRight(len, fill[0]));
        });

        t["center"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.center(s, len[, fill])");
            var len = ArgInt(args, 1, 0, "string.center(s, len[, fill])");
            var fill = args.Count > 2 && args[2].Type == DataType.String
                ? args[2].String : " ";

            if (src.Length >= len || fill.Length == 0) return DynValue.NewString(src);

            var total = len - src.Length;
            var left = total / 2;
            var right = total - left;

            return DynValue.NewString(
                src.PadLeft(src.Length + left, fill[0]).PadRight(len, fill[0]));
        });

        t["truncate"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.truncate(s, len[, suffix])");
            var len = ArgInt(args, 1, 0, "string.truncate(s, len[, suffix])");
            var suffix = args.Count > 2 && args[2].Type == DataType.String
                ? args[2].String : "…";

            if (src.Length <= len) return DynValue.NewString(src);
            return len <= suffix.Length
                ? DynValue.NewString(suffix.Substring(0, len))
                : DynValue.NewString(src.Substring(0, len - suffix.Length) + suffix);
        });

        // ---- 反转 ----

        t["reverse"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.reverse(s)");
            var arr = src.ToCharArray();
            Array.Reverse(arr);
            return DynValue.NewString(new string(arr));
        });

        // ---- 计数 / 位置 ----

        t["count"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.count(s, needle)");
            var needle = ArgString(args, 1, "string.count(s, needle)");
            if (needle.Length == 0) return DynValue.NewNumber(0);

            var n = 0;
            var i = 0;
            while (true)
            {
                var idx = src.IndexOf(needle, i, StringComparison.Ordinal);
                if (idx < 0) break;
                n++;
                i = idx + needle.Length;
            }
            return DynValue.NewNumber(n);
        });

        t["indexOf"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.indexOf(s, needle)");
            var needle = ArgString(args, 1, "string.indexOf(s, needle)");
            var idx = src.IndexOf(needle, StringComparison.Ordinal);
            return DynValue.NewNumber(idx < 0 ? -1 : idx + 1);
        });

        t["lastIndexOf"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.lastIndexOf(s, needle)");
            var needle = ArgString(args, 1, "string.lastIndexOf(s, needle)");
            var idx = src.LastIndexOf(needle, StringComparison.Ordinal);
            return DynValue.NewNumber(idx < 0 ? -1 : idx + 1);
        });


        t["humanize"] = DynValue.NewCallback((_, args) =>
        {
            var n = ArgNum(args, 0, "string.humanize(n)");
            return DynValue.NewString(Humanize(n));
        });

        t["formatBytes"] = DynValue.NewCallback((_, args) =>
        {
            var n = ArgNum(args, 0, "string.formatBytes(b)");
            return DynValue.NewString(FormatBytes(n));
        });

        t["alnumOnly"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.alnumOnly(s)");
            var sb = new StringBuilder(src.Length);
            foreach (var c in src)
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return DynValue.NewString(sb.ToString());
        });

        t["unquote"] = DynValue.NewCallback((_, args) =>
        {
            var src = ArgString(args, 0, "string.unquote(s)");
            if (src.Length >= 2)
            {
                var a = src[0];
                var b = src[src.Length - 1];
                if ((a == '"' && b == '"') || (a == '\'' && b == '\''))
                    return DynValue.NewString(src.Substring(1, src.Length - 2));
            }
            return DynValue.NewString(src);
        });

        return t;
    }
    private static DynValue UnaryString(Func<string, string> f, string sig)
    {
        return DynValue.NewCallback((_, args) => DynValue.NewString(f(ArgString(args, 0, sig))));
    }
    private static string ArgString(CallbackArguments args, int i, string sig)
    {
        return args.Count <= i || args[i].Type != DataType.String ? throw new ScriptRuntimeException(sig) : args[i].String;
    }
    private static int ArgInt(CallbackArguments args, int i, int fallback, string sig)
    {
        if (args.Count <= i) return fallback;
        return args[i].Type != DataType.Number ? throw new ScriptRuntimeException(sig) : (int)args[i].Number;
    }

    private static double ArgNum(CallbackArguments args, int i, string sig)
    {
        return args.Count <= i || args[i].Type != DataType.Number ? throw new ScriptRuntimeException(sig) : args[i].Number;
    }

    private static DynValue PackStringArray(Script s, string[] arr)
    {
        var t = new Table(s);
        for (var i = 0; i < arr.Length; i++)
            t[i + 1] = arr[i];
        return DynValue.NewTable(t);
    }

    private static string Humanize(double n)
    {
        var abs = Math.Abs(n);
        if (abs < 1000) return n.ToString("0.##");
        if (abs < 1e6) return (n / 1e3).ToString("0.0") + "k";
        return abs < 1e9 ? (n / 1e6).ToString("0.0") + "M" : (n / 1e9).ToString("0.0") + "B";
    }

    private static string FormatBytes(double b)
    {
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var i = 0;

        while (b >= 1024 && i < units.Length - 1)
        {
            b /= 1024;
            i++;
        }

        return i == 0
            ? ((long)b).ToString() + " " + units[i]
            : b.ToString("0.00") + " " + units[i];
    }
}