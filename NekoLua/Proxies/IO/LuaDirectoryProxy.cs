using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.IO;

/// <summary>
/// directory 表：
///   directory.create(path)
///   directory.delete(path)                  -- 递归
///   directory.exists / empty / count(path)
///   directory.list / listFiles / listDirectories(path)
///   directory.info(path)            -> {name, path, created, modified}
///   directory.move / copy(src, dst)
/// </summary>
public class LuaDirectoryProxy : IRegisterLua
{
    public string Name { get; } = "directory";

    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var t = new Table(script);

        t["create"] = DynValue.NewCallback((s, args) =>
        {
            var path = Resolve(mod, args, 0);
            Directory.CreateDirectory(path);
            return DynValue.True;
        });

        t["delete"] = DynValue.NewCallback((s, args) =>
        {
            var path = Resolve(mod, args, 0);
            if (!Directory.Exists(path))
                return DynValue.False;

            Directory.Delete(path, true);
            return DynValue.True;
        });

        t["exists"] = DynValue.NewCallback((s, args) =>
        {
            var path = Resolve(mod, args, 0);
            return DynValue.NewBoolean(Directory.Exists(path));
        });

        t["empty"] = DynValue.NewCallback((s, args) =>
        {
            var path = Resolve(mod, args, 0);
            return !Directory.Exists(path) ? DynValue.False : DynValue.NewBoolean(!Directory.EnumerateFileSystemEntries(path).Any());
        });

        t["count"] = DynValue.NewCallback((s, args) =>
        {
            var path = Resolve(mod, args, 0);
            return !Directory.Exists(path) ? DynValue.NewNumber(0) : DynValue.NewNumber(Directory.EnumerateFileSystemEntries(path).Count());
        });

        t["list"] = DynValue.NewCallback((s, args) => List(script, mod, args, ListMode.All));
        t["listFiles"] = DynValue.NewCallback((s, args) => List(script, mod, args, ListMode.Files));
        t["listDirectories"] = DynValue.NewCallback((s, args) => List(script, mod, args, ListMode.Directories));

        t["info"] = DynValue.NewCallback((s, args) =>
        {
            var path = Resolve(mod, args, 0);
            if (!Directory.Exists(path))
                return DynValue.Nil;

            var di = new DirectoryInfo(path);
            var result = new Table(script)
            {
                ["name"] = di.Name,
                ["path"] = di.FullName,
                ["created"] = di.CreationTime.ToString("O"),
                ["modified"] = di.LastWriteTime.ToString("O"),
            };
            return DynValue.NewTable(result);
        });

        t["move"] = DynValue.NewCallback((s, args) =>
        {
            var src = Resolve(mod, args, 0);
            var dst = Resolve(mod, args, 1);

            if (!Directory.Exists(src))
                return DynValue.False;

            Directory.Move(src, dst);
            return DynValue.True;
        });

        t["copy"] = DynValue.NewCallback((s, args) =>
        {
            var src = Resolve(mod, args, 0);
            var dst = Resolve(mod, args, 1);

            if (!Directory.Exists(src))
                return DynValue.False;

            CopyDirectory(src, dst);
            return DynValue.True;
        });

        return t;
    }
    private enum ListMode { All, Files, Directories }
    static DynValue List(Script script, LuaModInstance mod, CallbackArguments args, ListMode mode)
    {
        var path = Resolve(mod, args, 0);
        if (!Directory.Exists(path))
            return DynValue.Nil;

        var result = new Table(script);
        var i = 1;

        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            var isFile = File.Exists(entry);
            var isDir = Directory.Exists(entry);

            if (mode == ListMode.Files && !isFile)
                continue;
            if (mode == ListMode.Directories && !isDir)
                continue;

            result[i++] = DynValue.NewString(Path.GetFileName(entry));
        }

        return DynValue.NewTable(result);
    }
    static void CopyDirectory(string src, string dst)
    {
        _ = Directory.CreateDirectory(dst);

        foreach (var file in Directory.EnumerateFiles(src))
        {
            var target = Path.Combine(dst, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
        }

        foreach (var sub in Directory.EnumerateDirectories(src))
        {
            var target = Path.Combine(dst, Path.GetFileName(sub));
            CopyDirectory(sub, target);
        }
    }
    static string Resolve(LuaModInstance mod, CallbackArguments args, int index)
    {
        return args.Count <= index || args[index].Type != DataType.String
            ? throw new ScriptRuntimeException("directory: 路径参数需要字符串")
            : LuaPathGuard.Resolve(mod, args[index].String, "directory");
    }
}
