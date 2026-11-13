using Astra.Teams;

using MoonSharp.Interpreter;

using PlayerRoles;

using UnityEngine;

namespace NekoLua.AstraBridge;

internal sealed class TeamDefinition
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string ColorHex = "#FFFFFF";
    public CustomTeamRelation Relation = CustomTeamRelation.Custom;
    public Team VanillaTeam = Team.OtherAlive;
    public Faction VanillaFaction = Faction.Unclassified;
    public bool KeepVanilla;
    public RelationEntry[] Blacklist = [];
    public RelationEntry[] Whitelist = [];
}

internal sealed class RoleDefinition
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public RoleTypeId RoleType = RoleTypeId.None;
    public string TeamId = string.Empty;
    public float MaxHealth = 100f;
    public ItemType[] Items = [];
    public bool ClearInventory = true;
    public bool ShowCustomInfo;
    public Vector3? Scale;
    public Vector3? Gravity;
    public RoleSpawnFlags SpawnFlags = RoleSpawnFlags.All;
    public RoleChangeReason ChangeReason = RoleChangeReason.Respawn;
    public string[] Waves = [];
    public int WaveSpawnCount = 1;
    public Dictionary<string, int>? WaveCounts;
    public DynValue? OnAdded;
    public DynValue? OnRemoved;
}

internal sealed class WaveDefinition
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string[] Teams = [];
    public bool ModifyThreat = true;
    public float Threat = -1f;
    public bool SpawnProtect = true;
    public float SpawnProtectDuration = -1f;
    public string? Announcement;
    public string? AnnouncementTranslation;
    public Dictionary<string, int>? Table;
    public DynValue? AllowSpawn;
}

/// <summary>
/// Lua 定义表 → C# 不可变定义。
/// </summary>
/// <remarks>
/// 只接受白名单字段与枚举名：不解析程序集名 / 类型名，避免把脚本变成反射入口。
/// 任一字段非法时整条定义拒绝（不留半成品），错误信息带字段路径。
/// </remarks>
internal static class DefinitionParser
{
    public static TeamDefinition ParseTeam(Table t)
    {
        const string ctx = "astra.team.define";

        return new TeamDefinition
        {
            Id = RequireId(t, ctx),
            Name = LuaSpec.RequiredString(t, "name", ctx),
            ColorHex = LuaSpec.OptionalString(t, "color", ctx) ?? "#FFFFFF",
            Relation = LuaSpec.OptionalEnum(t, "relation", ctx, CustomTeamRelation.Custom),
            VanillaTeam = LuaSpec.OptionalEnum(t, "vanilla_team", ctx, Team.OtherAlive),
            VanillaFaction = LuaSpec.OptionalEnum(t, "vanilla_faction", ctx, Faction.Unclassified),
            KeepVanilla = LuaSpec.OptionalBool(t, "keep_vanilla", ctx, false),
            Blacklist = ParseRelations(t, "hostile", ctx),
            Whitelist = ParseRelations(t, "friendly", ctx),
        };
    }

    public static RoleDefinition ParseRole(Table t)
    {
        const string ctx = "astra.role.define";

        var def = new RoleDefinition
        {
            Id = RequireId(t, ctx),
            Name = LuaSpec.RequiredString(t, "name", ctx),
            Description = LuaSpec.OptionalString(t, "description", ctx) ?? string.Empty,
            RoleType = LuaSpec.OptionalEnum(t, "role_type", ctx, RoleTypeId.None),
            TeamId = LuaSpec.OptionalString(t, "team", ctx) ?? string.Empty,
            MaxHealth = LuaSpec.OptionalNumber(t, "max_health", ctx, 100f),
            Items = ParseItems(t, "items", ctx),
            ClearInventory = LuaSpec.OptionalBool(t, "clear_inventory", ctx, true),
            ShowCustomInfo = LuaSpec.OptionalBool(t, "show_custom_info", ctx, false),
            Scale = LuaSpec.OptionalVector3(t, "scale", ctx),
            Gravity = LuaSpec.OptionalVector3(t, "gravity", ctx),
            SpawnFlags = LuaSpec.OptionalEnum(t, "spawn_flags", ctx, RoleSpawnFlags.All),
            ChangeReason = LuaSpec.OptionalEnum(t, "change_reason", ctx, RoleChangeReason.Respawn),
            Waves = LuaSpec.OptionalStringArray(t, "waves", ctx),
            WaveSpawnCount = (int)LuaSpec.OptionalNumber(t, "wave_count", ctx, 1f),
            WaveCounts = LuaSpec.OptionalIntMap(t, "wave_counts", ctx),
            OnAdded = LuaSpec.OptionalCallback(t, "on_added", ctx),
            OnRemoved = LuaSpec.OptionalCallback(t, "on_removed", ctx),
        };

        if (def.WaveSpawnCount == 0 || def.WaveSpawnCount < -1)
            throw LuaSpec.Error(ctx, "wave_count 必须是正整数或 -1（填充剩余）");

        return def;
    }

    public static WaveDefinition ParseWave(Table t)
    {
        const string ctx = "astra.wave.define";

        return new WaveDefinition
        {
            Id = RequireId(t, ctx),
            Name = LuaSpec.RequiredString(t, "name", ctx),
            Teams = LuaSpec.OptionalStringArray(t, "teams", ctx),
            ModifyThreat = LuaSpec.OptionalBool(t, "modify_threat", ctx, true),
            Threat = LuaSpec.OptionalNumber(t, "threat", ctx, -1f),
            SpawnProtect = LuaSpec.OptionalBool(t, "spawn_protect", ctx, true),
            SpawnProtectDuration = LuaSpec.OptionalNumber(t, "spawn_protect_duration", ctx, -1f),
            Announcement = LuaSpec.OptionalString(t, "announcement", ctx),
            AnnouncementTranslation = LuaSpec.OptionalString(t, "announcement_translation", ctx),
            Table = LuaSpec.OptionalIntMap(t, "table", ctx),
            AllowSpawn = LuaSpec.OptionalCallback(t, "allow_spawn", ctx),
        };
    }

    private static string RequireId(Table t, string ctx)
    {
        var id = LuaSpec.RequiredString(t, "id", ctx);

        if (id.Length > 64)
            throw LuaSpec.Error(ctx, "id 长度不能超过 64");

        foreach (var ch in id)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '-')
                throw LuaSpec.Error(ctx, $"id '{id}' 只允许字母、数字、下划线与短横线");
        }

        return id;
    }

    private static RelationEntry[] ParseRelations(Table t, string key, string ctx)
    {
        var raw = LuaSpec.OptionalStringArray(t, key, ctx);
        var result = new RelationEntry[raw.Length];

        for (var i = 0; i < raw.Length; i++)
        {
            try
            {
                result[i] = RelationEntry.Parse(raw[i]);
            }
            catch (Exception ex)
            {
                throw LuaSpec.Error(ctx, $"{key}[{i + 1}] = '{raw[i]}' 不是合法目标: {ex.Message}");
            }
        }

        return result;
    }

    private static ItemType[] ParseItems(Table t, string key, string ctx)
    {
        var raw = LuaSpec.OptionalStringArray(t, key, ctx);
        var result = new ItemType[raw.Length];

        for (var i = 0; i < raw.Length; i++)
        {
            if (!Enum.TryParse(raw[i], true, out ItemType item) || !Enum.IsDefined(typeof(ItemType), item))
                throw LuaSpec.Error(ctx, $"{key}[{i + 1}] = '{raw[i]}' 不是合法 ItemType");

            result[i] = item;
        }

        return result;
    }
}
