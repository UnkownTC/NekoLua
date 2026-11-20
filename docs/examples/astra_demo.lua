-- NekoLua.Astra 示例：自定义阵营 / 角色 / 波次
-- 部署：SecretLab/NekoLua/mods/<端口>/astra_demo/main.lua
-- 前置：NekoLua/extensions/NekoLua.Astra.dll + AstraFramework >= 7.0.0

if astra == nil or not astra.available then
    print("[astra_demo] astra 不可用: " .. tostring(astra and astra.version))
    return
end

-- 阵营：混沌精英，敌对 MTF，友好原版混沌
astra.team.define {
    id = "chaos_elite",
    name = "混沌精英",
    color = "#FF8800",
    relation = "custom",
    vanilla_team = "ChaosInsurgency",
    keep_vanilla = false,
    friendly = { "ChaosInsurgency" },
    hostile = { "NineTailedFox" },
}

-- 角色：精英霰弹手（200 血，自带霰弹枪与医疗包）
astra.role.define {
    id = "chaos_elite_shotgunner",
    name = "精英霰弹手",
    description = "混沌精英单位",
    role_type = "ChaosRifleman",
    team = "chaos_elite",
    max_health = 200,
    items = { "Shotgun", "Medkit" },   -- 原版 ItemType；也可写自定义物品 Id（Astra 3.0 起）
    clear_inventory = true,
    show_custom_info = true,
    waves = { "chaos_wave" },
    wave_counts = { ["chaos_wave"] = 2 },
    on_added = function(ply)
        -- 显式传 id，on_removed 用同一个 id 清理（不传 id 时桥接会生成并返回新 id）
        astra.hint.show(ply, "你是混沌精英", "CenterTop", 5, 20, "chaos_elite_hint")
    end,
    on_removed = function(ply)
        astra.hint.clear(ply, "chaos_elite_hint")
    end,
}

-- 波次：只刷混沌精英
astra.wave.define {
    id = "chaos_wave",
    name = "混沌波次",
    teams = { "chaos_elite" },
    weight = 5,               -- 3.1+：加权调度权重
    min_players = 4,          -- 3.1+：触发所需最少存活人数
    announcement = "混沌精英入场",
    spawn_protect = true,
    spawn_protect_duration = 8,
    modify_threat = true,
    threat = 1.2,
}

-- 物品：回合开始时在轻收容区生成 2 个医疗包拾取物（7.0+）
astra.item.define {
    id = "chaos_supply",
    name = "混沌补给",
    description = "混沌精英投放的医疗包",
    base_item = "Medkit",
    spawn_points = {
        { zone = "LightContainment", x = 0, y = 1, z = 0, probability = 0.5 },
    },
    max_spawn_count = 2,
}

-- 每 120 秒刷一次（timer.every 会一直重复）
timer.every(120, "chaos_wave_timer", function()
    local spawned = astra.wave.spawn("chaos_wave", player.list())
    print("[astra_demo] 波次生成 " .. tostring(#spawned) .. " 人")
end)

-- Astra 扩展的射击事件（桥接在 OnLoad 注册了事件类 astraplayer）
eventhook.bind("astraplayer", "Shooting", "astra_demo_shooting", function(ev)
    -- ev.player / ev.claimedTarget / ev.isAllowed
    if ev.player == nil then
        return
    end
end)

print("[astra_demo] 已注册: " .. #astra.role.list() .. " 个角色, "
    .. #astra.item.list() .. " 个物品, " .. #astra.wave.list() .. " 个波次")
