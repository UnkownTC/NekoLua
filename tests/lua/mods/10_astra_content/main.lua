-- 10_astra_content：AstraFramework 桥接的内容定义与运行时 API
-- 命令：lua_astra（控制台 / RA）

local kit = require("neko_testkit")

-- 兼容旧版 testkit（没有 trace 时退化为空操作）
local trace = kit.trace or function() end

if astra == nil or not astra.available then
    log.error("[10_astra_content] astra 不可用，请确认 AstraFramework >= 3.2.0 与 NekoLua.Astra.dll")
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
    on_added = function(ply)
        storage.set("kit_role_added", true)
    end,
    on_removed = function(ply)
        storage.set("kit_role_removed", true)
    end,
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
    s:eq("wave.find", astra.wave.find("kit_wave").id, "kit_wave")

    s:eq("角色只注册一次", countWhere(astra.role.list(), "id", "kit_role"), 1)
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

    -- 以下需要在线玩家
    local ply = kit.firstPlayer()
    trace("after-firstPlayer ply=" .. tostring(ply ~= nil))
    if ply == nil then
        s:skip("role.give/get/remove", "没有在线玩家")
        s:skip("hint.show/clear", "没有在线玩家")
        s:skip("effect.apply/remove", "没有在线玩家")
    else
        trace("before-give")
        s:truthy("role.give", astra.role.give(ply, "kit_role"))
        trace("after-give")

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
