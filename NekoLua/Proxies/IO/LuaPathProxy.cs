using MoonSharp.Interpreter;

using NekoLua.Loader;
using NekoLua.Registers;

using NorthwoodLib.Pools;

namespace NekoLua.Proxies.IO;

/// <summary>
/// path 表（纯字符串操作，不触碰文件系统）：
///   path.combine(...)
///   path.full / normalize / extension / name / baseName / parent / root(str)
///   path.changeExtension(str, newExt)
///   path.isAbsolute / isRooted(str)     -> bool
///   path.temp() / randomFile()          -> string
///   path.separator / altSeparator       -- 主/备分隔符
/// </summary>
public class LuaPathProxy : IRegisterLua
{
    public string Name { get; } = "path";

    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);

        t["combine"] = DynValue.NewCallback((_, args) =>
        {
            var parts = new List<string>(args.Count);
            for (var i = 0; i < args.Count; i++)
            {
                if (args[i].Type != DataType.String)
                    throw new ScriptRuntimeException("path.combine(...): 所有参数需要字符串");
                parts.Add(args[i].String);
            }
            return DynValue.NewString(Path.Combine([.. parts]));
        });

        t["full"] = Str(s => Path.GetFullPath(s), "path.full(str)");
        t["normalize"] = Str(Normalize, "path.normalize(str)");
        t["extension"] = Str(Path.GetExtension, "path.extension(str)");
        t["name"] = Str(Path.GetFileName, "path.name(str)");
        t["baseName"] = Str(Path.GetFileNameWithoutExtension, "path.baseName(str)");
        t["parent"] = Str(s => Path.GetDirectoryName(s) ?? "", "path.parent(str)");
        t["root"] = Str(s => Path.GetPathRoot(s) ?? "", "path.root(str)");

        t["changeExtension"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.String)
                throw new ScriptRuntimeException("path.changeExtension(str, newExt)");
            return DynValue.NewString(Path.ChangeExtension(args[0].String, args[1].String));
        });

        t["isAbsolute"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                return DynValue.False;

            var p = args[0].String;
            var isAbs = Path.IsPathRooted(p)
                        && !p.StartsWith(Path.DirectorySeparatorChar.ToString())
                        && !p.StartsWith(Path.AltDirectorySeparatorChar.ToString());
            return DynValue.NewBoolean(isAbs);
        });

        t["isRooted"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                return DynValue.False;
            return DynValue.NewBoolean(Path.IsPathRooted(args[0].String));
        });

        t["temp"] = DynValue.NewCallback((_, _) => DynValue.NewString(Path.GetTempPath()));
        t["randomFile"] = DynValue.NewCallback((_, _) => DynValue.NewString(Path.GetRandomFileName()));

        t["separator"] = Path.DirectorySeparatorChar.ToString();
        t["altSeparator"] = Path.AltDirectorySeparatorChar.ToString();

        return t;
    }
    static DynValue Str(Func<string, string> f, string sig)
    {
        return DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException(sig);
            return DynValue.NewString(f(args[0].String));
        });
    }
    static string Normalize(string p)
    {
        if (string.IsNullOrEmpty(p))
            return p;
        var sep = Path.DirectorySeparatorChar;
        var alt = Path.AltDirectorySeparatorChar;
        p = p.Replace(alt, sep);

        var parts = p.Split(sep);
        var stack = ListPool<string>.Shared.Rent(parts.Length);

        foreach (var part in parts)
        {
            if (part == "" || part == ".")
                continue;
            if (part == "..")
            {
                if (stack.Count > 0 && stack[stack.Count - 1] != "..")
                    stack.RemoveAt(stack.Count - 1);
                else
                    stack.Add("..");
            }
            else
                stack.Add(part);
        }
        var result = string.Join(sep.ToString(), stack);
        ListPool<string>.Shared.Return(stack);
        return result;
    }
}
