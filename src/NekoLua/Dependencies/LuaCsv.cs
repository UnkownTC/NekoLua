using System.Text;

using MoonSharp.Interpreter;

using NorthwoodLib.Pools;

namespace NekoLua.Dependencies;

public static class LuaCsv
{
    public static DynValue FromCsv(Script script, string text, bool hasHeader = true)
    {
        var rows = ParseRows(text);
        var t = new Table(script);

        if (rows.Count == 0)
            return DynValue.NewTable(t);

        if (!hasHeader)
        {
            for (var i = 0; i < rows.Count; i++)
                t[i + 1] = RowToArray(script, rows[i]);
            return DynValue.NewTable(t);
        }

        var header = rows[0];
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var obj = new Table(script);
            for (var c = 0; c < header.Count && c < row.Count; c++)
                obj[header[c]] = row[c];
            t[r] = DynValue.NewTable(obj);
        }
        return DynValue.NewTable(t);
    }
    public static string ToCsv(DynValue v)
    {
        if (v.Type != DataType.Table)
            throw new ScriptRuntimeException("csv.encode: 需要 table");

        var rows = v.Table;
        var sb = StringBuilderPool.Shared.Rent();

        if (IsArrayOfArrays(rows))
        {
            for (var i = 1; i <= rows.Length; i++)
                WriteRow(sb, rows.Get(i).Table);
            return sb.ToString();
        }

        var keys = new List<string>();
        var first = rows.Get(1);
        if (first.Type == DataType.Table)
        {
            foreach (var p in first.Table.Pairs)
                if (p.Key.Type == DataType.String)
                    keys.Add(p.Key.String);
        }

        if (keys.Count == 0)
        {
            for (var i = 1; i <= rows.Length; i++)
                WriteRow(sb, rows.Get(i).Table);
            return sb.ToString();
        }

        WriteRow(sb, keys);

        for (var i = 1; i <= rows.Length; i++)
        {
            var row = rows.Get(i);
            if (row.Type != DataType.Table)
                continue;

            var values = new List<string>();
            foreach (var k in keys)
            {
                var cell = row.Table.Get(k);
                values.Add(CellToString(cell));
            }
            WriteRow(sb, values);
        }

        return StringBuilderPool.Shared.ToStringReturn(sb);
    }
    private static List<List<string>> ParseRows(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQ = false;
        var i = 0;

        while (i < text.Length)
        {
            var c = text[i];

            if (inQ)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i += 2;
                        continue;
                    }
                    inQ = false;
                    i++;
                    continue;
                }
                cell.Append(c);
                i++;
                continue;
            }

            if (c == '"')
            {
                inQ = true;
                i++;
                continue;
            }

            if (c == ',')
            {
                row.Add(cell.ToString());
                cell.Clear();
                i++;
                continue;
            }

            if (c == '\r')
            {
                i++;
                continue;
            }

            if (c == '\n')
            {
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row);
                row = [];
                i++;
                continue;
            }

            cell.Append(c);
            i++;
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }
    private static DynValue RowToArray(Script script, List<string> row)
    {
        var t = new Table(script);
        for (var i = 0; i < row.Count; i++)
            t[i + 1] = row[i];
        return DynValue.NewTable(t);
    }

    private static void WriteRow(StringBuilder sb, List<string> row)
    {
        for (var i = 0; i < row.Count; i++)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(EscapeCell(row[i]));
        }
        sb.Append('\n');
    }
    private static void WriteRow(StringBuilder sb, Table row)
    {
        var n = row.Length;
        for (var i = 1; i <= n; i++)
        {
            if (i > 1)
                sb.Append(',');
            sb.Append(EscapeCell(CellToString(row.Get(i))));
        }
        sb.Append('\n');
    }
    private static string EscapeCell(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
    private static string CellToString(DynValue v)
    {
        return v.Type switch
        {
            DataType.String => v.String,
            DataType.Number => v.Number == Math.Floor(v.Number)
                ? ((long)v.Number).ToString()
                : v.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DataType.Boolean => v.Boolean ? "true" : "false",
            DataType.Nil or DataType.Void => "",
            _ => v.ToPrintString(),
        };
    }
    private static bool IsArrayOfArrays(Table t)
    {
        var first = t.Get(1);
        return first.Type == DataType.Table && IsArray(first.Table);
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
}
