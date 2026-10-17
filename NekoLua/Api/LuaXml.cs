using MoonSharp.Interpreter;
using System.Xml.Linq;

namespace NekoLua.Api;

internal static class LuaXml
{
    private const int MaxDepth = LuaConstants.MaxDepth;

    public static DynValue FromXml(Script script, string xml)
    {
        var doc = XDocument.Parse(xml);
        return doc.Root is null ? DynValue.Nil : ElementToLua(script, doc.Root, 0);
    }
    private static DynValue ElementToLua(Script script, XElement el, int depth)
    {
        if (depth > MaxDepth)
            throw new ScriptRuntimeException($"xml.decode: 嵌套深度超过 {MaxDepth}");

        var children = el.Elements().ToList();
        var attrs = el.Attributes().ToList();

        var text = children.Count == 0 ? (el.Value?.Trim() ?? "") : "";

        if (children.Count == 0 && attrs.Count == 0)
            return string.IsNullOrEmpty(text) ? DynValue.NewTable(new Table(script)) : DynValue.NewString(text);

        var t = new Table(script);

        foreach (var a in attrs)
            t["@" + a.Name.LocalName] = a.Value;

        if (children.Count == 0)
        {
            if (!string.IsNullOrEmpty(text))
                t["#text"] = text;
            return DynValue.NewTable(t);
        }
        var groups = children.GroupBy(e => e.Name.LocalName).ToList();

        if (attrs.Count == 0 && groups.Count == 1 && groups[0].Count() > 1)
        {
            var arr = new Table(script);
            var i = 1;
            foreach (var c in groups[0])
                arr[i++] = ElementToLua(script, c, depth + 1);
            return DynValue.NewTable(arr);
        }

        foreach (var g in groups)
        {
            if (g.Count() == 1)
                t[g.Key] = ElementToLua(script, g.First(), depth + 1);
            else
            {
                var arr = new Table(script);
                var i = 1;
                foreach (var c in g)
                    arr[i++] = ElementToLua(script, c, depth + 1);
                t[g.Key] = DynValue.NewTable(arr);
            }
        }

        if (!string.IsNullOrEmpty(text) && children.Count == 0)
            t["#text"] = text;

        return DynValue.NewTable(t);
    }
    public static string ToXml(DynValue v, string rootName = "root")
    {
        var el = LuaToElement(v, rootName, 0, []);
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), el);
        return doc.ToString();
    }
    private static XElement LuaToElement(DynValue v, string name, int depth, HashSet<Table> path)
    {
        if (depth > MaxDepth)
            throw new ScriptRuntimeException($"xml.encode: 嵌套深度超过 {MaxDepth}");

        var el = new XElement(name);

        switch (v.Type)
        {
            case DataType.Nil:
            case DataType.Void:
                break;

            case DataType.String:
            case DataType.Number:
            case DataType.Boolean:
                el.Value = ScalarToString(v);
                break;

            case DataType.Table:
                FillElement(el, v.Table, depth, path);
                break;
        }

        return el;
    }
    private static void FillElement(XElement el, Table t, int depth, HashSet<Table> path)
    {
        if (!path.Add(t))
            throw new ScriptRuntimeException("xml.encode: 检测到循环引用（table 自包含）");

        try
        {
            if (IsPureArray(t))
            {
                var n = t.Length;
                for (var i = 1; i <= n; i++)
                    el.Add(LuaToElement(t.Get(i), "item", depth + 1, path));
                return;
            }

            foreach (var p in t.Pairs)
            {
                if (p.Key.Type != DataType.String)
                    continue;
                var key = p.Key.String;

                if (key.StartsWith("@"))
                {
                    el.SetAttributeValue(key.Substring(1), ScalarToString(p.Value));
                    continue;
                }

                if (key == "#text")
                {
                    el.Value = ScalarToString(p.Value);
                    continue;
                }

                if (p.Value.Type == DataType.Table && IsPureArray(p.Value.Table))
                {
                    var n = p.Value.Table.Length;
                    for (var i = 1; i <= n; i++)
                        el.Add(LuaToElement(p.Value.Table.Get(i), key, depth + 1, path));
                    continue;
                }

                if (p.Value.Type == DataType.Table)
                {
                    el.Add(LuaToElement(p.Value, key, depth + 1, path));
                    continue;
                }

                el.Add(new XElement(key, ScalarToString(p.Value)));
            }
        }
        finally
        {
            path.Remove(t);
        }
    }
    private static string ScalarToString(DynValue v)
    {
        return v.Type switch
        {
            DataType.String => v.String,
            DataType.Number => v.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DataType.Boolean => v.Boolean ? "true" : "false",
            _ => "",
        };
    }

    private static bool IsPureArray(Table t)
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
