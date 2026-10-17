using MoonSharp.Interpreter;
using System.Globalization;
using System.Text;

namespace NekoLua.Api;

internal static class LuaYaml
{
    private const int MaxDepth = LuaConstants.MaxDepth;

    private sealed class Line
    {
        public int Indent;
        public string Content = "";
    }
    public static DynValue FromYaml(Script script, string yaml)
    {
        var lines = ParseLines(yaml);
        var pos = 0;
        return lines.Count == 0
            ? DynValue.NewTable(new Table(script))
            : ParseBlock(script, lines, ref pos, 0, 0);
    }

    private static List<Line> ParseLines(string text)
    {
        var result = new List<Line>();

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var stripped = StripComment(raw);
            if (string.IsNullOrWhiteSpace(stripped))
                continue;

            var indent = 0;
            while (indent < stripped.Length && stripped[indent] == ' ')
                indent++;

            result.Add(new Line
            {
                Indent = indent,
                Content = stripped.Substring(indent).TrimEnd(),
            });
        }
        return result;
    }

    private static string StripComment(string line)
    {
        var inSingle = false;
        var inDouble = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\'' && !inDouble)
                inSingle = !inSingle;
            else if (c == '"' && !inSingle)
                inDouble = !inDouble;
            else if (c == '#' && !inSingle && !inDouble)
                return line.Substring(0, i);
        }
        return line;
    }

    private static DynValue ParseBlock(Script script, List<Line> lines, ref int pos, int minIndent, int depth)
    {
        return depth > MaxDepth
            ? throw new ScriptRuntimeException($"yaml.decode: 嵌套深度超过 {MaxDepth}")
            : pos < lines.Count && lines[pos].Indent >= minIndent
            && lines[pos].Content.StartsWith("- ")
            ? ParseList(script, lines, ref pos, lines[pos].Indent, depth)
            : ParseMap(script, lines, ref pos, minIndent, depth);
    }
    private static DynValue ParseMap(Script script, List<Line> lines, ref int pos, int minIndent, int depth)
    {
        var tbl = new Table(script);
        int? indent = null;

        while (pos < lines.Count)
        {
            var line = lines[pos];

            if (line.Indent < minIndent)
                break;
            if (line.Content.StartsWith("- "))
                break;

            indent ??= line.Indent;
            if (line.Indent != indent)
                break;

            var colon = line.Content.IndexOf(':');
            if (colon < 0)
            { pos++; continue; }

            var key = line.Content.Substring(0, colon).Trim();
            var val = line.Content.Substring(colon + 1).Trim();
            pos++;

            tbl[key] = string.IsNullOrEmpty(val)
                ? pos < lines.Count && lines[pos].Indent > indent ? ParseBlock(script, lines, ref pos, lines[pos].Indent, depth + 1) : DynValue.Nil
                : ParseScalar(script, val, depth);
        }

        return DynValue.NewTable(tbl);
    }

    private static DynValue ParseList(Script script, List<Line> lines, ref int pos, int minIndent, int depth)
    {
        var tbl = new Table(script);
        var i = 1;

        while (pos < lines.Count)
        {
            var line = lines[pos];
            if (line.Indent < minIndent)
                break;
            if (!line.Content.StartsWith("- "))
                break;

            var content = line.Content.Substring(2).Trim();
            pos++;

            if (string.IsNullOrEmpty(content))
            {
                tbl[i++] = pos < lines.Count && lines[pos].Indent > line.Indent ? ParseBlock(script, lines, ref pos, lines[pos].Indent, depth + 1) : DynValue.Nil;
                continue;
            }

            var colon = content.IndexOf(':');
            if (colon > 0 && !content.StartsWith("\"") && !content.StartsWith("'"))
            {
                var k = content.Substring(0, colon).Trim();
                var v = content.Substring(colon + 1).Trim();
                var item = new Table(script);

                item[k] = string.IsNullOrEmpty(v)
                    ? pos < lines.Count && lines[pos].Indent > line.Indent ? ParseBlock(script, lines, ref pos, lines[pos].Indent, depth + 1) : DynValue.Nil
                    : ParseScalar(script, v, depth);

                tbl[i++] = DynValue.NewTable(item);
                continue;
            }

            tbl[i++] = ParseScalar(script, content, depth);
        }

        return DynValue.NewTable(tbl);
    }

    private static DynValue ParseScalar(Script script, string val, int depth)
    {
        if (depth > MaxDepth)
            throw new ScriptRuntimeException($"yaml.decode: 嵌套深度超过 {MaxDepth}");

        if (string.IsNullOrEmpty(val))
            return DynValue.Nil;

        if (val is "~" or "null" or "Null" or "NULL")
            return DynValue.Nil;

        if (val is "true" or "True" or "yes" or "Yes" or "on")
            return DynValue.True;
        if (val is "false" or "False" or "no" or "No" or "off")
            return DynValue.False;

        var last = val.Length - 1;

        if (val.Length >= 2 &&
            ((val[0] == '"' && val[last] == '"') || (val[0] == '\'' && val[last] == '\'')))
            return DynValue.NewString(val.Substring(1, val.Length - 2));

        if (val.StartsWith("[") && val.EndsWith("]"))
        {
            var inner = val.Substring(1, val.Length - 2);
            var tbl = new Table(script);
            var items = SplitInline(inner);
            for (var i = 0; i < items.Count; i++)
                tbl[i + 1] = ParseScalar(script, items[i], depth + 1);
            return DynValue.NewTable(tbl);
        }

        if (val.StartsWith("{") && val.EndsWith("}"))
        {
            var inner = val.Substring(1, val.Length - 2);
            var tbl = new Table(script);
            foreach (var pair in SplitInline(inner))
            {
                var colon = pair.IndexOf(':');
                if (colon < 0)
                    continue;
                var k = pair.Substring(0, colon).Trim();
                var v = pair.Substring(colon + 1).Trim();
                tbl[k] = ParseScalar(script, v, depth + 1);
            }
            return DynValue.NewTable(tbl);
        }

        return double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var num)
            ? DynValue.NewNumber(num)
            : DynValue.NewString(val);
    }

    private static List<string> SplitInline(string s)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var depth = 0;
        var inSingle = false;
        var inDouble = false;

        foreach (var c in s)
        {
            if (c == '\'' && !inDouble)
                inSingle = !inSingle;
            else if (c == '"' && !inSingle)
                inDouble = !inDouble;

            if (!inSingle && !inDouble)
            {
                if (c is '[' or '{')
                    depth++;
                else if (c is ']' or '}')
                    depth--;
                else if (c == ',' && depth == 0)
                {
                    result.Add(sb.ToString().Trim());
                    _ = sb.Clear();
                    continue;
                }
            }
            _ = sb.Append(c);
        }

        if (sb.Length > 0)
            result.Add(sb.ToString().Trim());
        return result;
    }
    public static string ToYaml(DynValue v)
    {
        var sb = new StringBuilder();
        WriteValue(sb, v, 0, atRoot: true, 0, []);
        return sb.ToString();
    }

    private static void WriteValue(StringBuilder sb, DynValue v, int indent, bool atRoot, int depth, HashSet<Table> path)
    {
        if (depth > MaxDepth)
            throw new ScriptRuntimeException($"yaml.encode: 嵌套深度超过 {MaxDepth}");

        switch (v.Type)
        {
            case DataType.Table:
                WriteTable(sb, v.Table, indent, atRoot, depth, path);
                break;

            case DataType.String:
                _ = sb.Append(QuoteIfNeeded(v.String));
                break;

            case DataType.Number:
                _ = sb.Append(v.Number == Math.Floor(v.Number)
                    ? ((long)v.Number).ToString(CultureInfo.InvariantCulture)
                    : v.Number.ToString("R", CultureInfo.InvariantCulture));
                break;

            case DataType.Boolean:
                _ = sb.Append(v.Boolean ? "true" : "false");
                break;

            default:
                _ = sb.Append("null");
                break;
        }
    }

    private static void WriteTable(StringBuilder sb, Table t, int indent, bool atRoot, int depth, HashSet<Table> path)
    {
        if (!path.Add(t))
            throw new ScriptRuntimeException("yaml.encode: 检测到循环引用（table 自包含）");

        try
        {
            var pad = new string(' ', indent);
            var isArr = IsArray(t);

            if (isArr)
            {
                var n = t.Length;
                if (n == 0)
                { _ = sb.Append("[]"); return; }

                for (var i = 1; i <= n; i++)
                {
                    if (i > 1 || !atRoot)
                        _ = sb.Append('\n').Append(pad);
                    _ = sb.Append("- ");
                    WriteValue(sb, t.Get(i), indent + 2, atRoot: false, depth + 1, path);
                }
                return;
            }

            var first = true;
            foreach (var p in t.Pairs)
            {
                if (!first || !atRoot)
                    _ = sb.Append('\n').Append(pad);
                first = false;

                var key = p.Key.Type == DataType.String ? p.Key.String : p.Key.ToPrintString();
                _ = sb.Append(key).Append(':');

                var val = p.Value;
                if (val.Type == DataType.Table && val.Table.Pairs.Any())
                {
                    WriteValue(sb, val, indent + 2, atRoot: false, depth + 1, path);
                }
                else
                {
                    _ = sb.Append(' ');
                    WriteValue(sb, val, indent, atRoot: false, depth + 1, path);
                }
            }
        }
        finally
        {
            path.Remove(t);
        }
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
    private static string QuoteIfNeeded(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "\"\"";

        var needsQuote = s.Contains(':') || s.Contains('#') || s.Contains('\n')
                      || s.StartsWith(" ") || s.EndsWith(" ")
                      || s.StartsWith("[") || s.StartsWith("{")
                      || s == "true" || s == "false" || s == "null" || s == "~";

        return !needsQuote ? s : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
