using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.IO;

/// <summary>
/// file 表（相对路径以 mod 目录为基准，受 Config.BlockedPaths 限制）：
///   file.read(path)                 -> string | nil
///   file.write(path, text)          -> bool
///   file.append(path, text)         -> bool
///   file.exists / delete(path)      -> bool
///   file.size(path)                 -> number | nil
///   file.name / parent(path)        -> string
///   file.join(...)                  -> string
/// </summary>
public class LuaFileProxy : IRegisterLua
{
    public string Name { get; } = "file";

    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);

        t["read"] = DynValue.NewCallback((_, args) =>
        {
            var path = Resolve(mod, args, 0);
            return File.Exists(path)
                ? DynValue.NewString(File.ReadAllText(path))
                : DynValue.Nil;
        });

        t["write"] = DynValue.NewCallback((_, args) =>
        {
            var path = Resolve(mod, args, 0);
            var text = ArgString(args, 1) ?? "";
            EnsureDir(path);
            File.WriteAllText(path, text);
            return DynValue.True;
        });

        t["append"] = DynValue.NewCallback((_, args) =>
        {
            var path = Resolve(mod, args, 0);
            var text = ArgString(args, 1) ?? "";
            EnsureDir(path);
            File.AppendAllText(path, text);
            return DynValue.True;
        });

        t["exists"] = DynValue.NewCallback((_, args) =>
        {
            var path = Resolve(mod, args, 0);
            return DynValue.NewBoolean(File.Exists(path));
        });

        t["delete"] = DynValue.NewCallback((_, args) =>
        {
            var path = Resolve(mod, args, 0);
            if (!File.Exists(path))
                return DynValue.False;

            File.Delete(path);
            return DynValue.True;
        });

        t["size"] = DynValue.NewCallback((_, args) =>
        {
            var path = Resolve(mod, args, 0);
            return File.Exists(path)
                ? DynValue.NewNumber(new FileInfo(path).Length)
                : DynValue.Nil;
        });

        t["name"] = DynValue.NewCallback((_, args) =>
        {
            var path = Resolve(mod, args, 0);
            return DynValue.NewString(Path.GetFileName(path));
        });

        t["parent"] = DynValue.NewCallback((_, args) =>
        {
            var path = Resolve(mod, args, 0);
            var parent = Path.GetDirectoryName(path);
            return DynValue.NewString(parent ?? "");
        });

        t["join"] = DynValue.NewCallback((_, args) =>
        {
            var parts = new List<string>(args.Count);
            for (var i = 0; i < args.Count; i++)
            {
                if (args[i].Type != DataType.String)
                    throw new ScriptRuntimeException("file.join(...): 所有参数需要字符串");
                parts.Add(args[i].String);
            }
            return DynValue.NewString(Path.Combine([.. parts]));
        });

        return t;
    }
    static string Resolve(LuaModInstance mod, CallbackArguments args, int index)
    {
        return args.Count <= index || args[index].Type != DataType.String
            ? throw new ScriptRuntimeException("file: 路径参数需要字符串")
            : LuaPathGuard.Resolve(mod, args[index].String, "file");
    }
    static string? ArgString(CallbackArguments args, int index)
    {
        if (args.Count <= index)
            return null;

        var v = args[index];
        return v.Type is DataType.Nil or DataType.Void ? null : v.Type == DataType.String ? v.String : v.ToPrintString();
    }
    static void EnsureDir(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            _ = Directory.CreateDirectory(dir);
    }
}
