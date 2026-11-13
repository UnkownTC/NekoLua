using Astra.Roles;
using Astra.Teams;
using Astra.Waves;

using LabApi.Events.CustomHandlers;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;

using UnityEngine;

using Logger = LabApi.Features.Console.Logger;

namespace NekoLua.AstraBridge;

internal enum ContentKind
{
    Team,
    Wave,
    Role,
}

internal enum ContentState
{
    /// <summary>Astra 尚未完成自动注册，等待 Flush。</summary>
    Pending,

    /// <summary>已在 Astra 注册表中。</summary>
    Registered,

    /// <summary>所属 mod 已被清理，等待「一帧后确认」的注销。</summary>
    Orphaned,
}

internal sealed class ModContent
{
    public LuaModInstance Mod = null!;
    public LuaObjectCache Cache = null!;
    public readonly List<ContentEntry> Entries = [];
}

internal sealed class ContentEntry
{
    public ContentKind Kind;
    public string Id = string.Empty;
    public ContentState State;
    public bool Warned;
    public bool WasRegistered;
    public ModContent Owner = null!;
    public object? Adapter;
    public TeamDefinition? Team;
    public RoleDefinition? Role;
    public WaveDefinition? Wave;
}

/// <summary>
/// NekoLua ↔ AstraFramework 桥接核心：内容注册表、延迟提交、按 mod 回收、回调派发。
/// </summary>
/// <remarks>
/// 三条时序约束（见 docs/ASTRA-EXTENSION.md）：
/// 1. 定义先入队，等 Astra 自动注册完成后统一 Flush（否则 Astra 的 Initialize 早退）；
/// 2. 同 Id 重定义只替换定义对象，不重复注册（热重载无感）；
/// 3. mod 清理时先标孤儿，下一帧确认没有重定义才真正注销。
/// </remarks>
internal static class AstraBridge
{
    public const int ApiVersion = 1;

    /// <summary>桥接要求的最低 Astra 版本（3.2 起提供加权波次 / 支援池 / 原版波次标记）。</summary>
    public static readonly Version MinimumAstraVersion = new(3, 2, 0);

    private static readonly Dictionary<string, ContentEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ModContent> _mods = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Color> _colorCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LuaRoleSpawner _spawner = new();

    private static AstraBootstrap? _bootstrap;
    private static Version? _astraVersion;
    private static bool _available;
    private static bool _flushed;
    private static bool _shuttingDown;
    private static int _mainThreadId;
    private static int _retry;

    public static bool Available => _available;

    public static string AstraVersionText => _astraVersion is null ? "unknown" : _astraVersion.ToString();

    /// <summary>当前处于已注册状态的内容条数（诊断 / astra.flush() 返回值）。</summary>
    public static int RegisteredCount =>
        _entries.Values.Count(e => e.State == ContentState.Registered);

    /// <summary>扩展 OnLoad：版本校验、事件类注册、spawner 安装。</summary>
    public static void Load()
    {
        _mainThreadId = Environment.CurrentManagedThreadId;
        _shuttingDown = false;
        _retry = 0;

        try
        {
            _astraVersion = typeof(CustomRoleApi).Assembly.GetName().Version;
            _available = _astraVersion is not null && _astraVersion >= MinimumAstraVersion;
        }
        catch (Exception ex)
        {
            _available = false;
            Logger.Error($"[NekoLua.Astra] 无法解析 AstraFramework 版本: {ex.Message}");
        }

        if (!_available)
        {
            Logger.Error(
                $"[NekoLua.Astra] 需要 AstraFramework >= {MinimumAstraVersion}，当前: {AstraVersionText}；astra 表将不可用");
            return;
        }

        try
        {
            LuaEventHook.RegisterEventClass("astraplayer", typeof(Astra.Events.PlayerEvents));
        }
        catch (Exception ex)
        {
            Logger.Warn($"[NekoLua.Astra] 注册 Astra 事件类失败: {ex.Message}");
        }

        try
        {
            _bootstrap = new AstraBootstrap();
            CustomHandlersManager.RegisterEventsHandler(_bootstrap);
        }
        catch (Exception ex)
        {
            Logger.Error($"[NekoLua.Astra] 注册插件就绪处理器失败: {ex.Message}");
            _bootstrap = null;
        }

        try
        {
            CustomWaveApi.AddRoleSpawner(_spawner);
        }
        catch (Exception ex)
        {
            Logger.Error($"[NekoLua.Astra] 注册波次角色生成器失败: {ex.Message}");
        }
    }

    /// <summary>扩展 OnUnload：注销全部 Lua 内容并清空状态。</summary>
    public static void Unload()
    {
        _shuttingDown = true;

        if (_bootstrap is not null)
        {
            try
            {
                CustomHandlersManager.UnregisterEventsHandler(_bootstrap);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[NekoLua.Astra] 注销插件就绪处理器失败: {ex.Message}");
            }

            _bootstrap = null;
        }

        foreach (var entry in Ordered())
        {
            if (!entry.WasRegistered)
                continue;

            try
            {
                UnregisterEntry(entry);
            }
            catch (Exception ex)
            {
                Logger.Error($"[NekoLua.Astra] 注销 {entry.Kind} {entry.Id} 失败: {ex.Message}");
            }
        }

        foreach (var content in _mods.Values)
        {
            LuaCleanupRegistry.Forget(content.Mod);
            content.Cache.Dispose();
        }

        _entries.Clear();
        _mods.Clear();
        _flushed = false;
        _retry = 0;
        _colorCache.Clear();
    }

    /// <summary>取（或创建）某个 mod 的桥接上下文。</summary>
    public static ModContent GetMod(LuaModInstance mod)
    {
        if (_mods.TryGetValue(mod.ModDir, out var content))
            return content;

        content = new ModContent
        {
            Mod = mod,
            Cache = new LuaObjectCache(mod.Script),
        };

        _mods[mod.ModDir] = content;
        LuaCleanupRegistry.Register(mod, OnModCleanup);
        return content;
    }

    /// <summary>定义/更新一个阵营；返回是否已成功进入 Astra 注册表。</summary>
    public static bool DefineTeam(LuaModInstance mod, TeamDefinition definition)
    {
        var entry = Upsert(mod, ContentKind.Team, definition.Id, e =>
        {
            e.Team = definition;
            e.Adapter ??= new LuaCustomTeam(definition);
        });

        return entry.State == ContentState.Registered;
    }

    /// <summary>定义/更新一个波次。</summary>
    public static bool DefineWave(LuaModInstance mod, WaveDefinition definition)
    {
        var entry = Upsert(mod, ContentKind.Wave, definition.Id, e =>
        {
            e.Wave = definition;
            e.Adapter ??= new LuaCustomWave(definition);
        });

        return entry.State == ContentState.Registered;
    }

    /// <summary>定义/更新一个角色。</summary>
    public static bool DefineRole(LuaModInstance mod, RoleDefinition definition)
    {
        var entry = Upsert(mod, ContentKind.Role, definition.Id, e =>
        {
            e.Role = definition;
            e.Adapter ??= new LuaCustomRole(definition);
        });

        return entry.State == ContentState.Registered;
    }

    public static TeamDefinition? TeamDef(string id) =>
        _entries.TryGetValue(id, out var entry) ? entry.Team : null;

    public static RoleDefinition? RoleDef(string id) =>
        _entries.TryGetValue(id, out var entry) ? entry.Role : null;

    public static WaveDefinition? WaveDef(string id) =>
        _entries.TryGetValue(id, out var entry) ? entry.Wave : null;

    public static bool OwnsRole(string roleId) =>
        _entries.TryGetValue(roleId, out var entry) && entry.Kind == ContentKind.Role;

    /// <summary>提交所有 Pending 内容（Astra 自动注册完成后调用）。</summary>
    public static void Flush()
    {
        if (!_available)
            return;

        var progress = true;
        var rounds = 0;

        while (progress && rounds < 2)
        {
            progress = false;
            rounds++;

            foreach (var kind in new[] { ContentKind.Team, ContentKind.Wave, ContentKind.Role })
            {
                foreach (var entry in _entries.Values.ToArray())
                {
                    if (entry.Kind != kind || entry.State != ContentState.Pending)
                        continue;

                    progress |= TryRegister(entry);
                }
            }
        }

        _flushed = !_entries.Values.Any(e => e.State == ContentState.Pending);

        if (_flushed)
        {
            _retry = 0;
            return;
        }

        // Astra 初始化晚于插件就绪事件时（极端加载顺序），下一帧再试，最多 5 次。
        if (_retry < 5)
        {
            _retry++;
            _ = LuaMainThread.Queue(Flush);
        }
    }

    /// <summary>把玩家套用为指定自定义角色（波次生成器入口）。</summary>
    public static bool SpawnRole(string roleId, Player player)
    {
        EnsureMainThread("波次生成");
        return CustomRoleApi.AddRole(player, roleId) is not null;
    }

    public static void InvokeCallback(string contentId, DynValue? callback, Player player) =>
        Execute(contentId, callback, player);

    public static bool InvokePredicate(string contentId, DynValue? callback, Player player)
    {
        var result = Execute(contentId, callback, player);
        return result.Type == DataType.Boolean && result.Boolean;
    }

    public static void EnsureMainThread(string api)
    {
        if (_mainThreadId == 0)
            return;

        if (Environment.CurrentManagedThreadId != _mainThreadId)
            throw new ScriptRuntimeException($"{api}: 必须在主线程调用（当前为后台线程）");
    }

    public static Color ParseColor(string hex)
    {
        if (_colorCache.TryGetValue(hex, out var cached))
            return cached;

        var color = ColorUtility.TryParseHtmlString(hex, out var parsed) ? parsed : Color.white;
        _colorCache[hex] = color;
        return color;
    }

    private static DynValue Execute(string contentId, DynValue? callback, Player player)
    {
        if (callback is null || callback.Type != DataType.Function)
            return DynValue.Nil;

        if (!_entries.TryGetValue(contentId, out var entry) || entry.Owner is null)
            return DynValue.Nil;

        try
        {
            return callback.Function.Call(entry.Owner.Cache.Wrap(player));
        }
        catch (InterpreterException ex)
        {
            Logger.Error($"[{entry.Owner.Mod.Name}] astra 回调 {contentId} 异常: {ex.DecoratedMessage}");
        }
        catch (Exception ex)
        {
            Logger.Error($"[{entry.Owner.Mod.Name}] astra 回调 {contentId} 异常: {ex.Message}");
        }

        return DynValue.Nil;
    }

    private static ContentEntry Upsert(
        LuaModInstance mod,
        ContentKind kind,
        string id,
        Action<ContentEntry> apply)
    {
        var content = GetMod(mod);

        if (_entries.TryGetValue(id, out var existing))
        {
            if (!ReferenceEquals(existing.Owner.Mod, mod))
                throw new ScriptRuntimeException(
                    $"astra: Id '{id}' 已被模组 {existing.Owner.Mod.Name} 占用（不允许跨模组覆盖）");

            apply(existing);

            if (existing.State == ContentState.Orphaned && existing.WasRegistered)
                existing.State = ContentState.Registered;   // 下一帧的注销检查会跳过
            else if (existing.State == ContentState.Orphaned)
                existing.State = ContentState.Pending;

            if (_flushed && existing.State != ContentState.Registered)
                _ = TryRegister(existing);

            return existing;
        }

        var entry = new ContentEntry
        {
            Kind = kind,
            Id = id,
            Owner = content,
            State = ContentState.Pending,
        };

        apply(entry);
        _entries[id] = entry;
        content.Entries.Add(entry);

        if (_flushed)
            _ = TryRegister(entry);

        return entry;
    }

    private static bool TryRegister(ContentEntry entry)
    {
        var ok = entry.Kind switch
        {
            ContentKind.Team => entry.Adapter is LuaCustomTeam team && CustomTeamApi.Register(team),
            ContentKind.Wave => entry.Adapter is LuaCustomWave wave && CustomWaveApi.Register(wave),
            ContentKind.Role => entry.Adapter is LuaCustomRole role && CustomRoleApi.Register(role),
            _ => false,
        };

        if (ok)
        {
            entry.State = ContentState.Registered;
            entry.WasRegistered = true;
            return true;
        }

        if (!entry.Warned)
        {
            entry.Warned = true;
            Logger.Warn(
                $"[NekoLua.Astra] {entry.Kind} '{entry.Id}' 暂未注册成功（Astra 尚未初始化或 Id 冲突），将随 Flush 重试");
        }

        return false;
    }

    private static void OnModCleanup(LuaModInstance mod)
    {
        if (_shuttingDown)
            return;

        if (!_mods.TryGetValue(mod.ModDir, out var content))
            return;

        var changed = false;

        foreach (var entry in content.Entries)
        {
            if (entry.State == ContentState.Orphaned)
                continue;

            entry.State = ContentState.Orphaned;
            changed = true;
        }

        if (changed)
            _ = LuaMainThread.Queue(() => Sweep(content));
    }

    private static void Sweep(ModContent content)
    {
        // 注销顺序必须与依赖方向相反：角色 → 波次 → 阵营（Astra 拒绝注销仍被波次引用的阵营）。
        foreach (var kind in UnregisterOrder)
        {
            foreach (var entry in content.Entries.ToArray())
            {
                if (entry.Kind != kind || entry.State != ContentState.Orphaned)
                    continue;

                if (entry.WasRegistered)
                {
                    try
                    {
                        UnregisterEntry(entry);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[NekoLua.Astra] 注销 {entry.Kind} '{entry.Id}' 失败: {ex.Message}");
                    }
                }

                _ = _entries.Remove(entry.Id);
                _ = content.Entries.Remove(entry);
            }
        }

        if (content.Entries.Count > 0)
            return;

        _ = _mods.Remove(content.Mod.ModDir);
        LuaCleanupRegistry.Forget(content.Mod);
        content.Cache.Dispose();
    }

    private static readonly ContentKind[] UnregisterOrder =
        [ContentKind.Role, ContentKind.Wave, ContentKind.Team];

    private static IEnumerable<ContentEntry> Ordered()
    {
        foreach (var kind in UnregisterOrder)
        {
            foreach (var entry in _entries.Values.ToArray())
            {
                if (entry.Kind == kind)
                    yield return entry;
            }
        }
    }

    private static void UnregisterEntry(ContentEntry entry)
    {
        switch (entry.Kind)
        {
            case ContentKind.Role:
                RemoveRoleFromPlayers(entry.Id);
                _ = CustomRoleApi.Unregister(entry.Id);
                break;

            case ContentKind.Wave:
                _ = CustomWaveApi.Unregister(entry.Id);
                break;

            case ContentKind.Team:
                if (!CustomTeamApi.Unregister(entry.Id))
                    Logger.Warn($"[NekoLua.Astra] 阵营 '{entry.Id}' 仍被波次引用，注销被拒绝");
                break;
        }
    }

    private static void RemoveRoleFromPlayers(string id)
    {
        foreach (var player in Player.List)
        {
            var role = CustomRoleApi.GetRole(player);

            if (role is not null && string.Equals(role.Id, id, StringComparison.OrdinalIgnoreCase))
                CustomRoleApi.RemoveRole(player);
        }
    }
}

/// <summary>插件就绪回调：Astra 自动注册完成后提交 Lua 内容。</summary>
internal sealed class AstraBootstrap : CustomEventsHandler
{
    public override void OnServerPluginsEnabled()
    {
        base.OnServerPluginsEnabled();
        AstraBridge.Flush();
    }
}

/// <summary>波次角色生成器：只处理 NekoLua.Astra 注册的角色，其余交给链上的其它生成器。</summary>
/// <remarks>
/// 实现 V2 接口以接收 <see cref="RoleEntry"/>：原版角色条目返回 false，由 Astra 的兜底（或链上其它生成器）处理。
/// </remarks>
internal sealed class LuaRoleSpawner : ICustomWaveRoleSpawner, ICustomWaveRoleSpawnerV2
{
    public bool TrySpawn(string roleId, Player player)
    {
        if (!AstraBridge.OwnsRole(roleId))
            return false;

        return AstraBridge.SpawnRole(roleId, player);
    }

    public bool TrySpawn(RoleEntry role, Player player)
    {
        if (!role.IsCustom)
            return false;

        var id = role.CustomRole?.Id ?? role.CustomRoleId;

        if (string.IsNullOrWhiteSpace(id) || !AstraBridge.OwnsRole(id))
            return false;

        return AstraBridge.SpawnRole(id, player);
    }
}
