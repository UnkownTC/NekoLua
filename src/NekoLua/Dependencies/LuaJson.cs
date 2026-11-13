using MoonSharp.Interpreter;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NekoLua.Dependencies;

public static class LuaJson
{
    // 递归转换的深度上限：超过即抛可捕获的 ScriptRuntimeException，
    // 防止深嵌套/循环引用把主线程打成不可捕获的 StackOverflow（审查 P1-2）
    private const int MaxDepth = 128;

    public static DynValue FromJson(Script script, string json)
    {
        var token = JToken.Parse(json);
        return FromToken(script, token);
    }
    static DynValue FromToken(Script script, JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                {
                    var t = new Table(script);
                    foreach (var p in (JObject)token)
                        t[p.Key] = FromToken(script, p.Value);
                    return DynValue.NewTable(t);
                }
            case JTokenType.Array:
                {
                    var t = new Table(script);
                    var i = 1;
                    foreach (var item in (JArray)token)
                        t[i++] = FromToken(script, item);
                    return DynValue.NewTable(t);
                }
            case JTokenType.Integer:
                return DynValue.NewNumber(token.Value<long>());
            case JTokenType.Float:
                return DynValue.NewNumber(token.Value<double>());
            case JTokenType.String:
                return DynValue.NewString(token.Value<string>() ?? "");
            case JTokenType.Boolean:
                return DynValue.NewBoolean(token.Value<bool>());
            case JTokenType.Null:
            case JTokenType.Undefined:
            case JTokenType.None:
            default:
                return DynValue.Nil;
        }
    }

    public static string ToJson(DynValue v)
    {
        var token = ToToken(v);
        return token.ToString(Formatting.None);
    }

    public static JToken ToToken(DynValue v)
    {
        return ToToken(v, 0, []);
    }

    private static JToken ToToken(DynValue v, int depth, HashSet<Table> path)
    {
        if (depth > MaxDepth)
            throw new ScriptRuntimeException($"json.encode: 嵌套深度超过 {MaxDepth}");

        return v.Type switch
        {
            DataType.Table => TableToToken(v.Table, depth, path),
            DataType.String => new JValue(v.String),
            DataType.Number => NumberToToken(v.Number),
            DataType.Boolean => new JValue(v.Boolean),
            _ => JValue.CreateNull(),
        };
    }

    private static JToken TableToToken(Table t, int depth, HashSet<Table> path)
    {
        // 路径式环检测：同一 table 在当前递归路径上出现第二次即视为循环引用；
        // 分支间的合法共享（DAG）不受影响
        if (!path.Add(t))
            throw new ScriptRuntimeException("json.encode: 检测到循环引用（table 自包含）");

        try
        {
            if (IsArray(t))
            {
                var arr = new JArray();
                var n = t.Length;
                for (var i = 1; i <= n; i++)
                    arr.Add(ToToken(t.Get(i), depth + 1, path));
                return arr;
            }

            var obj = new JObject();
            foreach (var p in t.Pairs)
            {
                var key = p.Key.Type == DataType.String
                    ? p.Key.String
                    : p.Key.ToPrintString();
                obj[key] = ToToken(p.Value, depth + 1, path);
            }
            return obj;
        }
        finally
        {
            path.Remove(t);
        }
    }
    static JToken NumberToToken(double n)
    {
        return n == Math.Floor(n) && !double.IsInfinity(n) && n >= long.MinValue && n <= long.MaxValue ? new JValue((long)n) : new JValue(n);
    }
    static bool IsArray(Table t)
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
