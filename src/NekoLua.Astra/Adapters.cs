using Astra.Items;
using Astra.Roles;
using Astra.Teams;
using Astra.Waves;

using LabApi.Features.Wrappers;

using PlayerRoles;

using UnityEngine;

namespace NekoLua.AstraBridge;

/// <summary>
/// Lua 定义的阵营适配器：所有属性都实时读取当前定义，
/// 因此 mod 热重载只需替换定义对象，无需重建/重注册适配器。
/// </summary>
internal sealed class LuaCustomTeam : CustomTeam
{
    private readonly string _id;
    private readonly TeamDefinition _fallback;

    public LuaCustomTeam(TeamDefinition definition)
    {
        _id = definition.Id;
        _fallback = definition;
    }

    private TeamDefinition Def => AstraBridge.TeamDef(_id) ?? _fallback;

    public override string Id => _id;

    public override string Name => Def.Name;

    public override Color TeamColor => AstraBridge.ParseColor(Def.ColorHex);

    public override CustomTeamRelation Relation => Def.Relation;

    public override bool KeepVanillaRelate => Def.KeepVanilla;

    public override Team VanillaTeam => Def.VanillaTeam;

    public override Faction VanillaFaction => Def.VanillaFaction;

    public override IReadOnlyList<RelationEntry> Blacklist => Def.Blacklist;

    public override IReadOnlyList<RelationEntry> Whitelist => Def.Whitelist;
}

/// <summary>
/// Lua 定义的角色适配器。模板实例与玩家实例都只持有 Id，
/// 每次读取定义时取「当前版本」，实现长生命周期模板 + 可替换定义。
/// </summary>
internal sealed class LuaCustomRole : CustomRole, IWaveSpawnCountProvider
{
    private readonly string _id;
    private readonly RoleDefinition _fallback;

    public LuaCustomRole(RoleDefinition definition)
    {
        _id = definition.Id;
        _fallback = definition;
    }

    private RoleDefinition Def => AstraBridge.RoleDef(_id) ?? _fallback;

    public override string Id => _id;

    public override string Name => Def.Name;

    public override string Description
    {
        get => Def.Description;
        set { }
    }

    public override RoleTypeId RoleType
    {
        get => Def.RoleType;
        set { }
    }

    public override TeamRef Team
    {
        get
        {
            if (string.IsNullOrEmpty(Def.TeamId))
                return base.Team;

            var team = CustomTeamApi.Find(Def.TeamId);
            return team is null ? base.Team : TeamRef.FromCustom(team);
        }
    }

    public override RoleSpawnFlags SpawnFlags => Def.SpawnFlags;

    public override RoleChangeReason ChangeReason => Def.ChangeReason;

    public override Vector3 Scale => Def.Scale ?? base.Scale;

    public override Vector3 Gravity => Def.Gravity ?? base.Gravity;

    public override bool ShowCustomInfo
    {
        get => Def.ShowCustomInfo;
        set { }
    }

    public override float MaxHealth
    {
        get => Def.MaxHealth;
        set { }
    }

    public override IReadOnlyList<ItemEntry> Items
    {
        get => Def.Items;
        set { }
    }

    public override bool ClearInventory
    {
        get => Def.ClearInventory;
        set { }
    }

    public IReadOnlyList<CustomWave> Waves
    {
        get
        {
            List<CustomWave> waves = [];

            foreach (var waveId in Def.Waves)
            {
                var wave = CustomWaveApi.Find(waveId);

                if (wave is not null)
                    waves.Add(wave);
            }

            return waves;
        }
    }

    public int WaveSpawnCount => Def.WaveSpawnCount;

    public int GetWaveSpawnCount(CustomWave wave)
    {
        var counts = Def.WaveCounts;

        return counts is not null && counts.TryGetValue(wave.Id, out var count)
            ? count
            : Def.WaveSpawnCount;
    }

    public override CustomRole CreateInstance() => new LuaCustomRole(Def);

    protected override void OnAdded(Player player) => AstraBridge.InvokeCallback(_id, Def.OnAdded, player);

    protected override void OnRemoved(Player player) => AstraBridge.InvokeCallback(_id, Def.OnRemoved, player);
}

/// <summary>Lua 定义的波次适配器（3.1+ 同时实现加权波次契约）。</summary>
internal sealed class LuaCustomWave : CustomWave, IWeightedWave
{
    private readonly string _id;
    private readonly WaveDefinition _fallback;

    public LuaCustomWave(WaveDefinition definition)
    {
        _id = definition.Id;
        _fallback = definition;
    }

    private WaveDefinition Def => AstraBridge.WaveDef(_id) ?? _fallback;

    public override string Id => _id;

    public override string Name => Def.Name;

    public override IReadOnlyList<TeamRef> Teams
    {
        get
        {
            List<TeamRef> teams = [];

            foreach (var teamId in Def.Teams)
            {
                var custom = CustomTeamApi.Find(teamId);

                if (custom is not null)
                {
                    teams.Add(TeamRef.FromCustom(custom));
                    continue;
                }

                // 没找到自定义阵营时按原版 Team 枚举解析（允许自定义阵营与原版阵营混编）。
                if (Enum.TryParse(teamId, true, out Team vanilla) && Enum.IsDefined(typeof(Team), vanilla))
                    teams.Add(TeamRef.FromVanilla(vanilla));
            }

            return teams;
        }
    }

    public int Weight => Def.Weight;

    public int MinPlayers => Def.MinPlayers;

    public int MaxPlayers => Def.MaxPlayers;

    public override bool ModifyThreat => Def.ModifyThreat;

    public override float CustomThreat => Def.Threat;

    public override bool SpawnProtect => Def.SpawnProtect;

    public override float SpawnProtectDuration => Def.SpawnProtectDuration;

    public override string? Announcement => Def.Announcement;

    public override string? AnnouncementTranslation => Def.AnnouncementTranslation;

    public override IReadOnlyDictionary<RoleEntry, int> Table =>
        Def.Table is null ? base.Table : ResolveTable();

    private IReadOnlyDictionary<RoleEntry, int> ResolveTable()
    {
        Dictionary<RoleEntry, int> table = [];
        var source = Def.Table!;

        foreach (var pair in source)
        {
            // 键可以是自定义角色 Id 或原版 RoleTypeId 名；先按已注册自定义角色匹配，再退回原版角色。
            if (CustomRoleApi.TryGetRoleTemplate(pair.Key, out _))
            {
                table[RoleEntry.Custom(pair.Key)] = pair.Value;
                continue;
            }

            if (Enum.TryParse(pair.Key, true, out RoleTypeId vanilla) && Enum.IsDefined(typeof(RoleTypeId), vanilla))
            {
                table[RoleEntry.Vanilla(vanilla)] = pair.Value;
                continue;
            }

            // 尚未注册的自定义角色 Id：先登记条目，Astra 在刷新时按 Id 解析。
            table[RoleEntry.Custom(pair.Key)] = pair.Value;
        }

        return table;
    }

    public override bool CheckAllowSpawn(Player player) =>
        AstraBridge.InvokePredicate(_id, Def.AllowSpawn, player);
}
