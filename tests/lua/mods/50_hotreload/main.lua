-- 50_hotreload：热重载 / 资源回收验证
-- 命令：lua_hr（控制台 / RA）
--
-- 验证步骤：
--   1. lua_hr           记录当前快照
--   2. nekolua reload 50_hotreload
--   3. lua_hr           期望 loads +1，timer/eventhook/role 数量不增长

local kit = require("neko_testkit")

local loads = (storage.get("hr_loads") or 0) + 1
storage.set("hr_loads", loads)

timer.everyLate(30, "hr_tick", function()
    storage.set("hr_ticks", (storage.get("hr_ticks") or 0) + 1)
end)

eventhook.bind("server", "RoundRestarted", "hr_evt", function()
    storage.set("hr_round", true)
end)

if astra ~= nil and astra.available then
    astra.flush()
    astra.role.define {
        id = "hr_role",
        name = "热重载测试角色",
        role_type = "ChaosRifleman",
        max_health = 100,
    }
    astra.wave.define {
        id = "hr_wave",
        name = "热重载测试波次",
        teams = {},
        weight = 1,
        spawn_protect = false,
    }
end

local function countById(list, expected)
    local n = 0
    for _, item in ipairs(list) do
        if item.id == expected then
            n = n + 1
        end
    end
    return n
end

kit.command("lua_hr", "热重载回收测试", function()
    local s = kit.new("50_hotreload")

    if astra ~= nil and astra.available then
        astra.flush()
    end

    s:eq("timer 数量（reload 后应为 1）", timer.count(), 1)
    s:eq("eventhook 数量（reload 后应为 1）", eventhook.count(), 1)
    s:eq("eventbus 订阅数", eventbus.count(), 0)

    if astra ~= nil and astra.available then
        s:eq("角色不重复注册", countById(astra.role.list(), "hr_role"), 1)
        s:eq("波次不重复注册", countById(astra.wave.list(), "hr_wave"), 1)
    else
        s:skip("Astra 内容回收", "astra 不可用")
    end

    local previous = storage.get("hr_last_loads") or 0
    s:truthy("reload 后 load 计数增长", loads > previous)
    storage.set("hr_last_loads", loads)

    s:eq("本次 load 计数", loads, storage.get("hr_loads"))

    local _, summary = s:finish()
    return summary .. string.format("（loads=%d）", loads)
end)

log.info(string.format("[50_hotreload] 第 %d 次加载；执行 lua_hr 检查回收，然后 nekolua reload 50_hotreload", loads))
