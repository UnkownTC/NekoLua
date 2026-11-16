using Astra.Items;
using Astra.Positions;
using Astra.Teams;

using MapGeneration;

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

/// <summary>刷怪点定义：位置、可选朝向与生成概率。</summary>
internal sealed class SpawnPointSpec
{
    public PositionEntry Position;
    public Quaternion? Rotation;
    public float Probability = 1f;
}

internal sealed class RoleDefinition
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public RoleTypeId RoleType = RoleTypeId.None;
    public string TeamId = string.Empty;
    public float MaxHealth = 100f;
    public ItemEntry[] Items = [];
    public bool ClearInventory = true;
    public bool ShowCustomInfo;
    public Vector3? Scale;
    public Vector3? Gravity;
    public RoleSpawnFlags SpawnFlags = RoleSpawnFlags.All;
    public RoleChangeReason ChangeReason = RoleChangeReason.Respawn;
    public string[] Waves = [];
    public int WaveSpawnCount = 1;
    public Dictionary<string, int>? WaveCounts;
    public SpawnPointSpec[] SpawnPoints = [];
    public int MaxSpawnCount;
    public DynValue? OnAdded;
    public DynValue? OnRemoved;
}

internal sealed class WaveDefinition
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public string[] Teams = [];
    public int Weight;
    public int MinPlayers;
    public int MaxPlayers = -1;
    public bool ModifyThreat = true;
    public float Threat = -1f;
    public bool SpawnProtect = true;
    public float SpawnProtectDuration = -1f;
    public string? Announcement;
    public string? AnnouncementTranslation;
    public Dictionary<string, int>? Table;
    public SpawnPointSpec[] SpawnPoints = [];
    public int MaxSpawnCount;
    public DynValue? AllowSpawn;
}

/// <summary>Lua 定义的物品：承载原版物品类型、可选名称/描述/重量与回合开始刷怪点。</summary>
internal sealed class ItemDefinition
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public ItemType BaseItem;
    public string Description = string.Empty;
    public float Weight = -1f;
    public SpawnPointSpec[] SpawnPoints = [];
    public int MaxSpawnCount;
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

        var spawnPoints = ParseSpawnPoints(t, ctx);

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
            SpawnPoints = spawnPoints,
            MaxSpawnCount = ParseMaxSpawnCount(t, ctx, spawnPoints.Length),
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

        var spawnPoints = ParseSpawnPoints(t, ctx);

        return new WaveDefinition
        {
            Id = RequireId(t, ctx),
            Name = LuaSpec.RequiredString(t, "name", ctx),
            Teams = LuaSpec.OptionalStringArray(t, "teams", ctx),
            Weight = (int)LuaSpec.OptionalNumber(t, "weight", ctx, 0f),
            MinPlayers = (int)LuaSpec.OptionalNumber(t, "min_players", ctx, 0f),
            MaxPlayers = (int)LuaSpec.OptionalNumber(t, "max_players", ctx, -1f),
            ModifyThreat = LuaSpec.OptionalBool(t, "modify_threat", ctx, true),
            Threat = LuaSpec.OptionalNumber(t, "threat", ctx, -1f),
            SpawnProtect = LuaSpec.OptionalBool(t, "spawn_protect", ctx, true),
            SpawnProtectDuration = LuaSpec.OptionalNumber(t, "spawn_protect_duration", ctx, -1f),
            Announcement = LuaSpec.OptionalString(t, "announcement", ctx),
            AnnouncementTranslation = LuaSpec.OptionalString(t, "announcement_translation", ctx),
            Table = LuaSpec.OptionalIntMap(t, "table", ctx),
            SpawnPoints = spawnPoints,
            MaxSpawnCount = ParseMaxSpawnCount(t, ctx, spawnPoints.Length),
            AllowSpawn = LuaSpec.OptionalCallback(t, "allow_spawn", ctx),
        };
    }

    public static ItemDefinition ParseItem(Table t)
    {
        const string ctx = "astra.item.define";

        var spawnPoints = ParseSpawnPoints(t, ctx);
        var baseItem = LuaSpec.OptionalEnum(t, "base_item", ctx, ItemType.None);

        if (baseItem == ItemType.None)
            throw LuaSpec.Error(ctx, "base_item 必须是原版 ItemType 名（不能为空或 None）");

        return new ItemDefinition
        {
            Id = RequireId(t, ctx),
            Name = LuaSpec.RequiredString(t, "name", ctx),
            BaseItem = baseItem,
            Description = LuaSpec.OptionalString(t, "description", ctx) ?? string.Empty,
            Weight = LuaSpec.OptionalNumber(t, "weight", ctx, -1f),
            SpawnPoints = spawnPoints,
            MaxSpawnCount = ParseMaxSpawnCount(t, ctx, spawnPoints.Length),
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

    private static ItemEntry[] ParseItems(Table t, string key, string ctx)
    {
        var raw = LuaSpec.OptionalStringArray(t, key, ctx);
        var result = new ItemEntry[raw.Length];

        for (var i = 0; i < raw.Length; i++)
        {
            // 先按原版 ItemType 解析；不是原版枚举时视为自定义物品 Id（运行时由 Astra 解析并发放）。
            if (Enum.TryParse(raw[i], true, out ItemType item) && Enum.IsDefined(typeof(ItemType), item))
            {
                result[i] = ItemEntry.Vanilla(item);
                continue;
            }

            if (string.IsNullOrWhiteSpace(raw[i]) || raw[i].Length > 64)
                throw LuaSpec.Error(ctx, $"{key}[{i + 1}] 既不是合法 ItemType，也不是合法的自定义物品 Id");

            result[i] = ItemEntry.Custom(raw[i]);
        }

        return result;
    }

    private static int ParseMaxSpawnCount(Table t, string ctx, int pointCount)
    {
        var value = t.Get("max_spawn_count");

        if (value.IsNil())
            return pointCount;

        if (value.Type != DataType.Number)
            throw LuaSpec.Error(ctx, "max_spawn_count 必须是数字");

        var count = value.Number;

        if (double.IsNaN(count) || count < 0 || count > int.MaxValue || Math.Floor(count) != count)
            throw LuaSpec.Error(ctx, "max_spawn_count 必须是非负整数");

        return (int)count;
    }

    private static SpawnPointSpec[] ParseSpawnPoints(Table t, string ctx)
    {
        var value = t.Get("spawn_points");

        if (value.IsNil())
            return [];

        if (value.Type != DataType.Table)
            throw LuaSpec.Error(ctx, "spawn_points 必须是数组");

        List<SpawnPointSpec> result = [];
        var index = 0;

        foreach (var item in LuaSpec.Sequence(value.Table))
        {
            index++;

            if (item.Type != DataType.Table)
                throw LuaSpec.Error(ctx, $"spawn_points[{index}] 必须是表");

            result.Add(ParseSpawnPoint(item.Table, ctx, index));
        }

        return [.. result];
    }

    private static SpawnPointSpec ParseSpawnPoint(Table t, string ctx, int index)
    {
        var room = OptionalEnumOrNull<RoomName>(t, "room", ctx, index);
        var zone = OptionalEnumOrNull<FacilityZone>(t, "zone", ctx, index);
        var shape = OptionalEnumOrNull<RoomShape>(t, "shape", ctx, index);
        var local = ReadVector(t, ctx, index);

        var hasFilter = room.HasValue || zone.HasValue || shape.HasValue;
        var position = hasFilter
            ? PositionEntry.Find(room, zone, shape, local)
            : PositionEntry.Find(local);

        var yaw = OptionalNumber(t, "yaw", ctx, index);
        var probability = OptionalNumber(t, "probability", ctx, index) ?? 1f;

        if (probability < 0f || probability > 1f)
            throw LuaSpec.Error(ctx, $"spawn_points[{index}].probability 必须在 0~1 之间");

        return new SpawnPointSpec
        {
            Position = position,
            Rotation = yaw.HasValue ? Quaternion.Euler(0f, yaw.Value, 0f) : null,
            Probability = probability,
        };
    }

    private static Vector3 ReadVector(Table t, string ctx, int index)
    {
        var x = OptionalNumber(t, "x", ctx, index);
        var y = OptionalNumber(t, "y", ctx, index);
        var z = OptionalNumber(t, "z", ctx, index);

        return new Vector3(x ?? 0f, y ?? 0f, z ?? 0f);
    }

    private static float? OptionalNumber(Table t, string key, string ctx, int index)
    {
        var value = t.Get(key);

        if (value.IsNil())
            return null;

        if (value.Type != DataType.Number)
            throw LuaSpec.Error(ctx, $"spawn_points[{index}].{key} 必须是数字");

        var number = (float)value.Number;

        if (float.IsNaN(number) || float.IsInfinity(number))
            throw LuaSpec.Error(ctx, $"spawn_points[{index}].{key} 必须是有限数字");

        return number;
    }

    private static TEnum? OptionalEnumOrNull<TEnum>(Table t, string key, string ctx, int index)
        where TEnum : struct, Enum
    {
        var value = t.Get(key);

        if (value.IsNil())
            return null;

        if (value.Type != DataType.String)
            throw LuaSpec.Error(ctx, $"spawn_points[{index}].{key} 必须是字符串枚举名");

        if (!Enum.TryParse(value.String, true, out TEnum parsed) || !Enum.IsDefined(typeof(TEnum), parsed))
            throw LuaSpec.Error(ctx, $"spawn_points[{index}].{key} = '{value.String}' 不是合法取值");

        return parsed;
    }
}

/// <summary>把定义里的刷怪点折叠成 Astra 需要的只读字典（同一位置合并为最大概率）。</summary>
internal static class SpawnPointMapping
{
    public static IReadOnlyDictionary<SpawnPointEntry, float> Build(IEnumerable<SpawnPointSpec> specs)
    {
        Dictionary<SpawnPointEntry, float> result = [];

        foreach (var spec in specs)
        {
            var entry = new SpawnPointEntry(spec.Position, spec.Rotation);

            if (result.ContainsKey(entry))
                result[entry] = Math.Max(result[entry], spec.Probability);
            else
                result.Add(entry, spec.Probability);
        }

        return result;
    }
}
