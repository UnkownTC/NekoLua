-- 20_astra_errors：Astra 桥接的参数校验与失败路径（负向测试）
-- 命令：lua_err（控制台 / RA）

local kit = require("neko_testkit")

if astra == nil or not astra.available then
    log.error("[20_astra_errors] astra 不可用，跳过")
    return
end

astra.role.define {
    id = "kit_err_role",
    name = "错误测试角色",
    role_type = "ChaosRifleman",
    max_health = 100,
}

local function countById(list, expected)
    local n = 0
    for _, item in ipairs(list) do
        if item.id == expected then
            n = n + 1
        end
    end
    return n
end

kit.command("lua_err", "Astra 桥接负向测试", function()
    local s = kit.new("20_astra_errors")

    astra.flush()

    s:throws("role.define 空表", function()
        astra.role.define({})
    end, "astra.role.define")

    s:throws("role.define 缺 name", function()
        astra.role.define({ id = "kit_bad_name" })
    end, "name")

    s:throws("role.define 坏 role_type", function()
        astra.role.define({ id = "kit_bad_role", name = "x", role_type = "NotARole" })
    end, "role_type")

    s:throws("role.define 坏 items", function()
        astra.role.define({ id = "kit_bad_items", name = "x", items = { 1 } })
    end, "items")

    s:throws("role.define 坏 wave_count", function()
        astra.role.define({ id = "kit_bad_wave", name = "x", wave_count = 0 })
    end, "wave_count")

    s:throws("team.define 缺 id", function()
        astra.team.define({ name = "没有 id" })
    end, "id")

    s:throws("wave.define 坏 teams 类型", function()
        astra.wave.define({ id = "kit_bad_wave2", name = "x", teams = "not_a_table" })
    end, "teams")

    s:throws("role.define 坏 spawn_points 类型", function()
        astra.role.define({ id = "kit_bad_spawn", name = "x", spawn_points = "not_a_table" })
    end, "spawn_points")

    s:throws("role.define 坏 probability", function()
        astra.role.define({
            id = "kit_bad_spawn2",
            name = "x",
            spawn_points = { { probability = 2 } },
        })
    end, "probability")

    s:throws("wave.define 坏房间名", function()
        astra.wave.define({
            id = "kit_bad_room",
            name = "x",
            spawn_points = { { room = "NotARoom" } },
        })
    end, "room")

    s:throws("item.define 缺 base_item", function()
        astra.item.define({ id = "kit_bad_item", name = "x" })
    end, "base_item")

    s:throws("item.define 坏 base_item", function()
        astra.item.define({ id = "kit_bad_item2", name = "x", base_item = "NotAnItem" })
    end, "base_item")

    s:throws("item.define 坏 spawn_points 类型", function()
        astra.item.define({ id = "kit_bad_item3", name = "x", base_item = "Coin", spawn_points = "nope" })
    end, "spawn_points")

    s:run("同 Id 跨类型定义（角色先占用）", function()
        astra.role.define({ id = "kit_cross_id", name = "跨类型角色" })
    end)
    s:throws("item.define 复用角色 Id", function()
        astra.item.define({ id = "kit_cross_id", name = "x", base_item = "Coin" })
    end, "同模组的")

    s:throws("position.resolve 坏 x", function()
        astra.position.resolve({ x = "not_a_number" })
    end, "position.x")

    s:throws("position.resolve 坏房间名", function()
        astra.position.resolve({ room = "NotARoom" })
    end, "position.room")

    s:throws("position.teleport 无玩家", function()
        astra.position.teleport(nil, { x = 0, y = 0, z = 0 })
    end, "astra.position.teleport")

    s:throws("role.give 无玩家", function()
        astra.role.give(nil, "kit_err_role")
    end, "astra.role.give")

    s:throws("wave.spawn 未知波次", function()
        astra.wave.spawn("kit_missing_wave", {})
    end, "找不到波次")

    s:throws("scheduler.load 未知波次", function()
        local sched = astra.wave.scheduler()
        sched.load({ "kit_missing_wave" })
    end, "找不到波次")

    s:throws("scheduler.support_register 未知波次", function()
        local sched = astra.wave.scheduler()
        sched.support_register("kit_missing_wave")
    end, "找不到波次")

    -- 同模组同 Id 重定义允许，且不会产生重复登记
    s:run("同 Id 重定义", function()
        astra.role.define({
            id = "kit_err_role",
            name = "错误测试角色 V2",
            role_type = "ChaosRifleman",
            max_health = 120,
        })
    end)
    s:eq("重定义后只注册一次", countById(astra.role.list(), "kit_err_role"), 1)

    local ply = kit.firstPlayer()
    if ply == nil then
        s:skip("effect 未知枚举", "没有在线玩家")
    else
        s:throws("effect 未知枚举", function()
            astra.effect.apply(ply, { { effect = "NotAnEffect" } })
        end, "effect")
    end

    local _, summary = s:finish()
    return summary
end)

log.info("[20_astra_errors] 已加载；执行 lua_err 运行负向测试")
