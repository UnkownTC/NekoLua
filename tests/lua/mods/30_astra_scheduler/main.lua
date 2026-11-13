-- 30_astra_scheduler：加权波次调度器与支援池
-- 命令：lua_sched（控制台 / RA）

local kit = require("neko_testkit")

if astra == nil or not astra.available then
    log.error("[30_astra_scheduler] astra 不可用，跳过")
    return
end

astra.wave.define {
    id = "kit_w_high",
    name = "高权重波次",
    teams = {},
    weight = 10,
    min_players = 0,
    spawn_protect = false,
}

astra.wave.define {
    id = "kit_w_gate",
    name = "人数门槛波次",
    teams = {},
    weight = 5,
    min_players = 99,
    spawn_protect = false,
}

astra.wave.define {
    id = "kit_w_low",
    name = "低权重波次",
    teams = {},
    weight = 1,
    min_players = 0,
    spawn_protect = false,
}

kit.command("lua_sched", "Astra 加权波次调度测试", function()
    local s = kit.new("30_astra_scheduler")

    astra.flush()

    local sched = astra.wave.scheduler()

    s:eq("load 数量", sched.load({ "kit_w_high", "kit_w_gate", "kit_w_low" }), 3)
    s:eq("remaining 初始", sched.remaining(), 3)

    local first = sched.advance(0)
    s:eq("高权重优先", first and first.id or "nil", "kit_w_high")
    s:eq("remaining 1 次后", sched.remaining(), 2)

    -- kit_w_gate 要求 99 人，0 人时应被跳过并消耗游标
    local second = sched.advance(0)
    s:eq("跳过人数门槛", second and second.id or "nil", "kit_w_low")
    s:eq("remaining 2 次后", sched.remaining(), 0)

    s:eq("队列耗尽", sched.advance(0), nil)
    s:eq("耗尽后 current", sched.current(), nil)

    sched.reset()
    s:eq("reset 后 remaining", sched.remaining(), 0)

    -- 支援池
    s:truthy("support_register", sched.support_register("kit_w_low", 5))
    local support = sched.support_pick(0)
    s:eq("support_pick 结果", support and support.id or "nil", "kit_w_low")
    s:eq("support_pick 后 current", sched.current() and sched.current().id or "nil", "kit_w_low")

    s:throws("support_register 未知波次", function()
        sched.support_register("kit_missing_wave")
    end, "找不到波次")

    s:eq("load 空表", sched.load({}), 0)

    local _, summary = s:finish()
    return summary
end)

log.info("[30_astra_scheduler] 已加载；执行 lua_sched 运行调度测试")
