-- 10_astra_content：AstraFramework 桥接的内容定义与运行时 API
-- 命令：lua_astra（控制台 / RA）

local kit = require("neko_testkit")

-- 兼容旧版 testkit（没有 trace 时退化为空操作）
local trace = kit.trace or function() end

if astra == nil or not astra.available then
    log.error("[10_astra_content] astra 不可用，请确认 AstraFramework >= 8.0.0 与 NekoLua.Astra.dll")
    return
end

-- 阵营
astra.team.define {
    id = "kit_team",
    name = "测试阵营",
    color = "#33AAFF",
    relation = "custom",
    vanilla_team = "ChaosInsurgency",
    keep_vanilla = false,
    friendly = { "ChaosInsurgency" },
}

-- 角色
astra.role.define {
    id = "kit_role",
    name = "测试角色",
    description = "NekoLua 测试用自定义角色",
    role_type = "ChaosRifleman",
    team = "kit_team",
    max_health = 150,
    items = { "Medkit" },
    clear_inventory = true,
    show_custom_info = true,
    waves = { "kit_wave" },
    wave_count = 1,
    spawn_points = {
        { x = 0, y = 0, z = 0, yaw = 90 },
    },
    min_amount = 0,
    max_spawn_count = 0,
    can_spawn = function() return true end,
    on_added = function(ply)
        storage.set("kit_role_added", true)
    end,
    on_removed = function(ply)
        storage.set("kit_role_removed", true)
    end,
}

-- 物品（回合开始刷怪点；max_spawn_count=0 避免测试服实际生成）
astra.item.define {
    id = "kit_item",
    name = "测试物品",
    description = "NekoLua 测试用自定义物品",
    base_item = "Coin",
    spawn_points = {
        { x = 0, y = 0, z = 0, probability = 0.5 },
    },
    min_amount = 0,
    max_spawn_count = 0,
    can_spawn = function() return true end,
}

-- 波次
astra.wave.define {
    id = "kit_wave",
    name = "测试波次",
    teams = { "kit_team" },
    weight = 3,
    min_players = 0,
    announcement = "测试波次",
    spawn_protect = false,
    spawn_points = {
        { zone = "LightContainment", x = 0, y = 0, z = 0, probability = 0.25 },
    },
    min_amount = 0,
    max_spawn_count = 0,
    can_spawn = function() return true end,
}

local function countWhere(list, idField, expected)
    local n = 0
    for _, item in ipairs(list) do
        if item[idField] == expected then
            n = n + 1
        end
    end
    return n
end

kit.command("lua_astra", "Astra 桥接内容与运行时测试", function()
    local s = kit.new("10_astra_content")

    file.write("trace.txt", "")
    trace("start")

    astra.flush()
    trace("flushed")

    s:truthy("astra.available", astra.available)
    s:truthy("astra.version 非空", type(astra.version) == "string" and #astra.version > 0)
    s:eq("astra.api_version", astra.api_version, 1)

    s:eq("team.find", astra.team.find("kit_team").id, "kit_team")
    s:eq("role.find", astra.role.find("kit_role").id, "kit_role")
    s:eq("item.find", astra.item.find("kit_item").id, "kit_item")
    s:eq("wave.find", astra.wave.find("kit_wave").id, "kit_wave")

    s:eq("角色只注册一次", countWhere(astra.role.list(), "id", "kit_role"), 1)
    s:eq("物品只注册一次", countWhere(astra.item.list(), "id", "kit_item"), 1)
    s:eq("波次只注册一次", countWhere(astra.wave.list(), "id", "kit_wave"), 1)

    -- 空玩家表 + 波次表为空：返回空数组且不抛错
    s:eq("wave.spawn 空玩家", #astra.wave.spawn("kit_wave", {}), 0)
    trace("after-wave-spawn")

    -- 事件类扩展
    s:run("eventhook astraplayer 绑定/解绑", function()
        eventhook.bind("astraplayer", "Shooting", "kit_astra_evt", function() end)
        eventhook.unbind("kit_astra_evt")
    end)
    trace("after-eventhook")

    -- 倒计时
    local countdownId = astra.countdown.start(0.1, function()
        storage.set("kit_countdown_fired", true)
    end)
    s:typeIs("countdown.start 返回 id", countdownId, "number")
    astra.countdown.cancel(countdownId)
    trace("after-countdown")

    -- 音频（缺失文件应返回 0 而不是抛错）
    s:run("audio.play 缺失文件", function()
        astra.audio.play("kit_missing_audio.ogg")
    end)
    trace("after-audio")

    -- 位置解析（世界坐标永远可解析；筛选条目在无匹配房间时返回 nil）
    local resolved = astra.position.resolve({ x = 1, y = 2, z = 3, yaw = 45 })
    s:typeIs("position.resolve 返回表", resolved, "table")
    if resolved ~= nil then
        s:eq("position.resolve y", resolved.y, 2)
        s:truthy("position.resolve 保留 yaw", resolved.yaw ~= nil)
    end
    trace("after-position")

    -- 以下需要在线玩家
    local ply = kit.firstPlayer()
    trace("after-firstPlayer ply=" .. tostring(ply ~= nil))
    if ply == nil then
        s:skip("role.give/get/remove", "没有在线玩家")
        s:skip("item.give", "没有在线玩家")
        s:skip("hint.show/clear", "没有在线玩家")
        s:skip("effect.apply/remove", "没有在线玩家")
    else
        trace("before-give")
        s:truthy("role.give", astra.role.give(ply, "kit_role"))
        trace("after-give")

        s:truthy("item.give", astra.item.give(ply, "kit_item"))
        trace("after-item-give")

        s:eq("role.get", astra.role.get(ply).id, "kit_role")
        trace("after-get")

        s:truthy("on_added 回调", storage.get("kit_role_added") == true)
        trace("after-onadded-check")

        trace("before-hint")
        s:run("hint.show/clear", function()
            -- show 返回实际 messageId；clear 必须用同一个 id（HintServiceMeow 对空/不存在 id 会抛 NRE）
            astra.hint.clear(ply, astra.hint.show(ply, "NekoLua 测试提示", "CenterTop", 2))
        end)
        trace("after-hint")

        trace("before-effect")
        s:run("effect.apply/remove", function()
            astra.effect.apply(ply, { { effect = "Bleeding", intensity = 1, duration = 1 } })
            astra.effect.remove(ply, { { effect = "Bleeding", intensity = 1, duration = 1 } })
        end)
        trace("after-effect")

        trace("before-remove")
        astra.role.remove(ply)
        trace("after-remove")

        s:falsy("role.remove 后 get", astra.role.get(ply) ~= nil)
        s:truthy("on_removed 回调", storage.get("kit_role_removed") == true)
        trace("after-gameplay-block")
    end

    local _, summary = s:finish()
    trace("finish")
    return summary
end)

log.info("[10_astra_content] 已加载；执行 lua_astra 运行内容测试")
