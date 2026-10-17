using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;
using System.Text.RegularExpressions;

namespace NekoLua.Proxies.Text;

/// <summary>
/// template 表 —— 字符串模板替换（占位符语法 {{path}}）：
///
///   template.render(str, vars)      -> string
///     一次性渲染
///     vars 支持点路径：{{user.profile.name}}
///     找不到的 key 保留原文 {{xxx}}
///
///   template.compile(str)           -> function
///     编译成可复用函数：(vars) -> string
/// </summary>
internal class LuaTemplateProxy : IRegisterLua
{
    public string Name { get; } = "template";
    private static readonly Regex PlaceholderRegex =
        new(@"\{\{([^{}]+)\}\}", RegexOptions.Compiled);
    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);

        t["render"] = DynValue.NewCallback((_, args) =>
        {
            return args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.Table
                ? throw new ScriptRuntimeException("template.render(str, vars)")
                : DynValue.NewString(Render(args[0].String, args[1].Table));
        });

        t["compile"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("template.compile(str)");

            var template = args[0].String;

            return DynValue.NewCallback((_, callArgs) =>
            {
                return callArgs.Count < 1 || callArgs[0].Type != DataType.Table
                    ? throw new ScriptRuntimeException("compiled(vars): vars 需要 table")
                    : DynValue.NewString(Render(template, callArgs[0].Table));
            });
        });

        return t;
    }
    private static string Render(string template, Table vars)
    {
        return PlaceholderRegex.Replace(template, match =>
        {
            var path = match.Groups[1].Value.Trim();
            return ResolvePath(vars, path) ?? match.Value;
        });
    }
    private static string? ResolvePath(Table vars, string path)
    {
        var cur = DynValue.NewTable(vars);
        var start = 0;

        while (start < path.Length)
        {
            var dot = path.IndexOf('.', start);
            var seg = dot < 0 ? path.Substring(start) : path.Substring(start, dot - start);
            if (seg.Length == 0)
                return null;

            if (cur.Type != DataType.Table)
                return null;

            cur = cur.Table.Get(seg);
            if (cur.Type is DataType.Nil or DataType.Void)
                return null;

            if (dot < 0)
                break;
            start = dot + 1;
        }

        return cur.Type switch
        {
            DataType.String => cur.String,
            DataType.Number => cur.Number == Math.Floor(cur.Number)
                ? ((long)cur.Number).ToString()
                : cur.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DataType.Boolean => cur.Boolean ? "true" : "false",
            DataType.Table => null,
            _ => null,
        };
    }
}
