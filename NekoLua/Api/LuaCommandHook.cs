using CommandSystem;

using LabApi.Features.Console;

using MoonSharp.Interpreter;
using NekoLua.Api.Enums;
using NekoLua.Api.Structures;
using NekoLua.Api.Structures.Commands;
using NekoLua.Api.TypeScanner;
using NekoLua.Loader;

using LabServer = LabApi.Features.Wrappers.Server;

namespace NekoLua.Api;

internal sealed class LuaCommandHook
{
    private static readonly List<LuaCommandHook> _all = [];
    private readonly LuaModInstance _mod;
    private readonly Dictionary<(CommandTarget Target, string Name), ICommand> _topLevel = [];
    public LuaCommandHook(LuaModInstance mod)
    {
        _mod = mod;
        _all.Add(this);
    }
    public int Count => _topLevel.Count;
    public void RegisterSingle(CommandTarget target, LuaCommandSpec spec)
    {
        if (spec.Callback is null)
            throw new ScriptRuntimeException($"命令 '{spec.Name}' 缺少 callback");

        EnsureSlotFree(target, spec.Name);

        var cmd = CreateLeaf(spec, this);

        try
        { GetHandler(target).RegisterCommand(cmd); }
        catch (ArgumentException)
        {
            var aliasInfo = spec.Aliases.Length > 0
                ? $"\n  别名: {string.Join(", ", spec.Aliases)}"
                : "";
            throw new ScriptRuntimeException(
                $"命令 '{spec.Name}' 与已有命令冲突（命令名或别名重复）{aliasInfo}");
        }
        catch (Exception ex)
        {
            throw new ScriptRuntimeException($"命令 '{spec.Name}' 注册失败 -> {ex}");
        }

        _topLevel[(target, spec.Name)] = cmd;
    }

    public void RegisterParent(CommandTarget target, LuaCommandSpec parentSpec, LuaCommandSpec[] childSpecs)
    {
        if (childSpecs.Length == 0)
            throw new ScriptRuntimeException($"父命令 '{parentSpec.Name}' 至少需要一个子命令");

        foreach (var child in childSpecs)
            if (child.Callback is null)
                throw new ScriptRuntimeException($"子命令 '{child.Name}' 缺少 callback");

        EnsureSlotFree(target, parentSpec.Name);

        var parent = CreateParent(parentSpec, this);

        foreach (var childSpec in childSpecs)
            parent.RegisterCommand(CreateLeaf(childSpec, this));

        try
        { GetHandler(target).RegisterCommand(parent); }
        catch (Exception ex)
        { throw new ScriptRuntimeException($"父命令 '{parentSpec.Name}' 注册失败 -> {ex}"); }

        _topLevel[(target, parentSpec.Name)] = parent;
    }
    private void EnsureSlotFree(CommandTarget target, string name)
    {
        if (_topLevel.ContainsKey((target, name)))
            throw new ScriptRuntimeException($"命令 '{name}' 已在 {target} 注册");
    }
    public int Unregister(string name)
    {
        var keys = _topLevel.Keys.Where(k => k.Name == name).ToArray();
        var n = 0;
        foreach (var (Target, Name) in keys)
            if (Unregister(Target, name))
                n++;
        return n;
    }
    public bool Unregister(CommandTarget target, string name)
    {
        if (!_topLevel.TryGetValue((target, name), out var cmd))
            return false;

        try
        { GetHandler(target).UnregisterCommand(cmd); }
        catch (Exception ex)
        {
            Logger.Warn($"[{_mod.Name}] 反注册 '{name}' ({target}) 失败: {ex.Message}");
        }

        _ = _topLevel.Remove((target, name));
        return true;
    }
    public int UnregisterAll()
    {
        var total = 0;
        foreach (var (Target, Name) in _topLevel.Keys.ToArray())
            if (Unregister(Target, Name))
                total++;
        return total;
    }
    public IEnumerable<string> Names => _topLevel.Keys.Select(k => k.Name).Distinct();
    private static ICommandHandler GetHandler(CommandTarget target)
    {
        return target switch
        {
            CommandTarget.Console => LabServer.GameConsoleCommandHandler,
            CommandTarget.RemoteAdmin => LabServer.RemoteAdminCommandHandler,
            CommandTarget.Client => LabServer.ClientCommandHandler,
            _ => throw new ScriptRuntimeException($"未知 command target: {target}"),
        };
    }

    private static CommandBase CreateLeaf(LuaCommandSpec spec, LuaCommandHook owner)
    {
        var hasUsage = spec.Usages.Length > 0;

        return (spec.IsHide, hasUsage) switch
        {
            (false, false) => new PlainCommand { Spec = spec, Owner = owner },
            (true, false) => new HiddenCommand { Spec = spec, Owner = owner },
            (false, true) => new UsageCommand { Spec = spec, Owner = owner },
            (true, true) => new HiddenUsageCommand { Spec = spec, Owner = owner },
        };
    }
    private static ParentCommandBase CreateParent(LuaCommandSpec spec, LuaCommandHook owner)
    {
        var hasUsage = spec.Usages.Length > 0;

        return (spec.IsHide, hasUsage) switch
        {
            (false, false) => new PlainParent { Spec = spec, Owner = owner },
            (true, false) => new HiddenParent { Spec = spec, Owner = owner },
            (false, true) => new UsageParent { Spec = spec, Owner = owner },
            (true, true) => new HiddenUsageParent { Spec = spec, Owner = owner },
        };
    }
    internal bool ExecuteCallback(DynValue callback, ArraySegment<string> arguments,
                              ICommandSender sender, out string response)
    {
        try
        {
            var script = _mod.Script;

            var luaArgs = new Table(script);
            for (var i = 0; i < arguments.Count; i++)
                luaArgs[i + 1] = arguments.Array![arguments.Offset + i];

            var senderTable = new LuaTypeMapper(script)
                .UseProxy()
                .ScanInstance(sender)
                .Build();

            try
            {
                var player = LabApi.Features.Wrappers.Player.Get(sender);
                if (player != null)
                {
                    var cache = GetOrCreateCache(script);
                    senderTable["player"] = cache.Wrap(player);
                }
            }
            catch
            {

            }

            var result = callback.Function.Call(
                DynValue.NewTable(luaArgs),
                DynValue.NewTable(senderTable));

            return ParseResult(result, out response);
        }
        catch (InterpreterException ex)
        {
            Logger.Error($"[{_mod.Name}] 命令回调异常: {ex.DecoratedMessage}");
            response = $"命令执行失败: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error($"[{_mod.Name}] 命令回调异常: {ex.Message}");
            response = $"命令执行失败: {ex.Message}";
            return false;
        }
    }
    private LuaObjectCache? _cache;
    private LuaObjectCache GetOrCreateCache(Script script)
    {
        return _cache ??= new LuaObjectCache(script);
    }

    private static bool ParseResult(DynValue result, out string response)
    {
        response = "";

        switch (result.Type)
        {
            case DataType.Nil:
            case DataType.Void:
                return true;

            case DataType.Boolean:
                return result.Boolean;

            case DataType.String:
                response = result.String;
                return true;

            case DataType.Tuple:
                {
                    var t = result.Tuple;
                    var success = true;
                    var msg = "";

                    for (var i = 0; i < t.Length; i++)
                    {
                        var v = t[i];
                        if (v.Type == DataType.Boolean)
                            success = v.Boolean;
                        else if (v.Type == DataType.String)
                            msg = v.String;
                        else if (v.Type == DataType.Number)
                            msg = v.Number.ToString();
                    }

                    response = msg;
                    return success;
                }

            default:
                response = result.ToPrintString();
                return true;
        }
    }
    public static void CleanupFor(LuaModInstance mod)
    {
        foreach (var h in _all.Where(h => ReferenceEquals(h._mod, mod)).ToArray())
        {
            _ = h.UnregisterAll();
            h._cache?.Dispose();
            _ = _all.Remove(h);
        }
    }
    public static void ClearAll()
    {
        foreach (var h in _all)
            _ = h.UnregisterAll();
        _all.Clear();
    }
}
