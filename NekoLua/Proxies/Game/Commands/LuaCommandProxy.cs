using MoonSharp.Interpreter;
using NekoLua.Api;
using NekoLua.Api.Enums;
using NekoLua.Api.Structures;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Game.Commands;

/// <summary>
/// command 表：
///   command.register(name, desc, callback, targets[, options])
///   command.register({spec}, targets)
///   command.register({parentSpec, childSpec, ...}, targets)   -- 父子命令
///
///   targets：{"console", "remoteAdmin", "client"}
///            简写 {"con", "ra", "cli"}
///
///   options：{aliases = {...}, isHide = bool, usages = {...}}
///
///   command.unregister(name)                    -> number 从所有平台
///   command.unregisterConsole / unregisterRemoteAdmin / unregisterClient(name)
///   command.unregisterAll()                     -> number
///   command.count()                             -> number
///
/// 回调签名：function(args, sender) -> success?, response?
///   sender.player 在 RA/Client 命令里可用
/// </summary>
internal class LuaCommandProxy : IRegisterLua
{
    public string Name { get; } = "command";
    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);
        var hook = new LuaCommandHook(mod);

        t["register"] = DynValue.NewCallback((_, args) =>
        {
            try
            {
                var (parentSpec, childSpecs, isParent, targets) = ParseRegisterArgs(args);
                var succeeded = 0;
                var errors = new List<string>();

                foreach (var target in targets)
                {
                    try
                    {
                        if (isParent)
                            hook.RegisterParent(target, parentSpec, childSpecs!);
                        else
                            hook.RegisterSingle(target, parentSpec);
                        succeeded++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{target}: {ex.Message}");
                    }
                }

                return succeeded == 0 ? Fail($"所有平台注册失败: {string.Join("; ", errors)}") : Ok();
            }
            catch (Exception ex)
            {
                return Fail(ex.Message);
            }
        });

        t["unregister"] = DynValue.NewCallback((_, args) =>
        {
            return args.Count < 1 || args[0].Type != DataType.String
                ? throw new ScriptRuntimeException("command.unregister(name)")
                : DynValue.NewNumber(hook.Unregister(args[0].String));
        });

        t["unregisterAll"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(hook.UnregisterAll()));

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(hook.Count));

        return t;
    }

    private static (LuaCommandSpec Parent, LuaCommandSpec[]? Children, bool IsParent, CommandTarget[] Targets)
        ParseRegisterArgs(CallbackArguments args)
    {
        if (args.Count < 2)
            throw new ScriptRuntimeException(
                "command.register(commands, targets[, options])");

        if (args[0].Type == DataType.String)
        {
            if (args.Count < 4 ||
                args[1].Type != DataType.String ||
                args[2].Type != DataType.Function ||
                args[3].Type != DataType.Table)
                throw new ScriptRuntimeException(
                    "command.register(name, desc, callback, targets[, options])");

            var targets = ParseTargets(args[3], "command.register");
            var spec = ParsePositional(args, optionsIndex: 4, "command.register");

            return (spec, null, false, targets);
        }

        if (args[0].Type == DataType.Table)
        {
            if (args[1].Type != DataType.Table)
                throw new ScriptRuntimeException(
                    "command.register({spec}, targets) 或 command.register({specs[]}, targets)");

            var targets = ParseTargets(args[1], "command.register");
            var table = args[0].Table;
            var nameV = table.Get("name");

            if (nameV.Type == DataType.String)
            {
                var spec = ParseSpec(table, "command.register");
                return (spec, null, false, targets);
            }

            var specs = ParseSpecArray(table, "command.register");
            if (specs.Length < 2)
                throw new ScriptRuntimeException(
                    "command.register: 父子模式至少需要 1 个父命令和 1 个子命令");

            var children = new LuaCommandSpec[specs.Length - 1];
            Array.Copy(specs, 1, children, 0, children.Length);

            return (specs[0], children, true, targets);
        }

        throw new ScriptRuntimeException(
            "command.register: 第一个参数需要字符串或 table");
    }
    private static CommandTarget[] ParseTargets(DynValue v, string sig)
    {
        if (v.Type != DataType.Table)
            throw new ScriptRuntimeException($"{sig}: targets 需要字符串数组");

        var table = v.Table;
        var n = table.Length;

        if (n == 0)
            throw new ScriptRuntimeException($"{sig}: targets 不能为空");

        var list = new List<CommandTarget>();

        for (var i = 1; i <= n; i++)
        {
            var item = table.Get(i);
            if (item.Type != DataType.String)
                throw new ScriptRuntimeException(
                    $"{sig}: targets[{i}] 需要字符串");

            var target = ParseTarget(item.String, sig);
            if (!list.Contains(target))
                list.Add(target);
        }

        return [.. list];
    }
    private static CommandTarget ParseTarget(string s, string sig)
    {
        return s.ToLowerInvariant() switch
        {
            "console" or "con" => CommandTarget.Console,
            "remoteadmin" or "ra" or "admin" => CommandTarget.RemoteAdmin,
            "client" or "cli" or "player" => CommandTarget.Client,
            _ => throw new ScriptRuntimeException(
                    $"{sig}: 未知 target '{s}'，可选: console / remoteAdmin / client"),
        };
    }

    private static LuaCommandSpec ParsePositional(CallbackArguments args, int optionsIndex, string _)
    {
        string[] aliases;
        bool isHide;
        string[] usages;

        if (args.Count > optionsIndex &&
            args[optionsIndex].Type == DataType.Table &&
            IsOptionsTable(args[optionsIndex].Table))
        {
            var opts = args[optionsIndex].Table;
            aliases = ReadStringArray(opts, "aliases");
            isHide = ReadBool(opts, "isHide");
            usages = ReadStringArray(opts, "usages");
        }
        else
        {
            aliases = ParseStringArray(args, optionsIndex);
            isHide = args.Count > optionsIndex + 1 &&
                      args[optionsIndex + 1].Type == DataType.Boolean &&
                      args[optionsIndex + 1].Boolean;
            usages = ParseStringArray(args, optionsIndex + 2);
        }

        return new LuaCommandSpec
        {
            Name = args[0].String,
            Description = args[1].String,
            Aliases = aliases,
            IsHide = isHide,
            Usages = usages,
            Callback = args[2],
        };
    }
    private static LuaCommandSpec ParseSpec(Table t, string sig)
    {
        var name = OptString(t, "name");
        if (string.IsNullOrWhiteSpace(name))
            throw new ScriptRuntimeException($"{sig}: 命令缺少 name");

        var callback = t.Get("callback");
        DynValue? cb = null;
        if (callback.Type == DataType.Function)
            cb = callback;
        else if (callback.Type is not DataType.Nil and not DataType.Void)
            throw new ScriptRuntimeException($"{sig}: callback 需要函数或 nil");

        return new LuaCommandSpec
        {
            Name = name!,
            Description = OptString(t, "desc") ?? "",
            Aliases = OptStringArray(t, "aliases"),
            IsHide = OptBool(t, "hidden"),
            Usages = OptUsage(t, "usage"),
            Callback = cb,
        };
    }
    private static LuaCommandSpec[] ParseSpecArray(Table t, string sig)
    {
        var list = new List<LuaCommandSpec>();
        var n = t.Length;

        for (var i = 1; i <= n; i++)
        {
            var v = t.Get(i);
            if (v.Type != DataType.Table)
                throw new ScriptRuntimeException($"{sig}: 数组第 {i} 个元素不是 table");

            list.Add(ParseSpec(v.Table, sig));
        }

        return [.. list];
    }
    static DynValue Ok()
    {
        return DynValue.NewTuple(DynValue.True, DynValue.Nil);
    }

    static DynValue Fail(string msg)
    {
        return DynValue.NewTuple(DynValue.False, DynValue.NewString(msg));
    }

    static bool IsOptionsTable(Table t)
    {
        foreach (var p in t.Pairs)
        {
            if (p.Key.Type != DataType.String)
                continue;
            var k = p.Key.String;
            if (k is "aliases" or "isHide" or "usages")
                return true;
        }
        return false;
    }
    static string? OptString(Table t, string key)
    {
        var v = t.Get(key);
        return v.Type == DataType.String ? v.String : null;
    }
    static string[] OptStringArray(Table t, string key)
    {
        var v = t.Get(key);
        return v.Type == DataType.Table ? ReadTableAsStrings(v.Table) : [];
    }
    static string[] OptUsage(Table t, string key)
    {
        var v = t.Get(key);
        return v.Type == DataType.String ? [v.String] : v.Type == DataType.Table ? ReadTableAsStrings(v.Table) : [];
    }
    static bool OptBool(Table t, string key)
    {
        var v = t.Get(key);
        return v.Type == DataType.Boolean && v.Boolean;
    }
    static string[] ParseStringArray(CallbackArguments args, int index)
    {
        return args.Count <= index || args[index].Type != DataType.Table ? [] : ReadTableAsStrings(args[index].Table);
    }
    static string[] ReadTableAsStrings(Table t)
    {
        var list = new List<string>();
        var n = t.Length;
        for (var i = 1; i <= n; i++)
        {
            var v = t.Get(i);
            if (v.Type == DataType.String)
                list.Add(v.String);
        }
        return [.. list];
    }
    static string[] ReadStringArray(Table opts, string key)
    {
        var v = opts.Get(key);
        return v.Type == DataType.Table ? ReadTableAsStrings(v.Table) : [];
    }
    static bool ReadBool(Table opts, string key)
    {
        var v = opts.Get(key);
        return v.Type == DataType.Boolean && v.Boolean;
    }
}
