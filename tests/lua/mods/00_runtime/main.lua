-- 00_runtime：NekoLua 运行时基础功能冒烟
-- 命令：lua_rt（控制台 / RA）

local kit = require("neko_testkit")

kit.command("lua_rt", "NekoLua 运行时冒烟测试", function()
    local s = kit.new("00_runtime")

    s:typeIs("nekolua 表", nekolua, "table")
    s:truthy("nekolua.version 非空", type(nekolua.version) == "string" and #nekolua.version > 0)
    s:typeIs("player.count()", player.count(), "number")
    s:typeIs("player.list()", player.list(), "table")

    -- storage
    storage.set("kit_probe", { a = 1 })
    s:eq("storage.get", storage.get("kit_probe").a, 1)
    s:truthy("storage.has", storage.has("kit_probe"))
    s:truthy("storage.delete", storage.delete("kit_probe"))
    s:falsy("storage 删除后 has", storage.has("kit_probe"))

    -- json
    local encoded = json.encode({ x = 1, s = "ok" })
    s:truthy("json.encode", type(encoded) == "string")
    s:eq("json 往返", json.decode(encoded).x, 1)
    s:truthy("json.isValid 合法", json.isValid('{"a":1}'))
    s:falsy("json.isValid 非法", json.isValid("{oops"))

    -- eventbus
    local hit = 0
    eventbus.on("kit.topic", "kit_listener", function() hit = hit + 1 end)
    s:eq("eventbus.emit 送达", eventbus.emit("kit.topic", { n = 1 }), 1)
    s:eq("eventbus 回调", hit, 1)
    eventbus.off("kit.topic", "kit_listener")
    s:eq("eventbus 退订后", eventbus.emit("kit.topic", {}), 0)

    -- eventhook
    s:truthy("eventhook.classes 非空", #eventhook.classes() > 0)
    local before = eventhook.count()
    eventhook.bind("server", "RoundRestarted", "kit_probe", function() end)
    s:eq("eventhook.bind", eventhook.count(), before + 1)
    eventhook.unbind("kit_probe")
    s:eq("eventhook.unbind", eventhook.count(), before)

    -- timer
    timer.delay(0.2, "kit_delay", function() storage.set("kit_timer_fired", true) end)
    s:truthy("timer 调度", timer.count() >= 1)
    timer.cancel("kit_delay")

    s:truthy("command.count", command.count() >= 1)

    local _, summary = s:finish()
    return summary
end)

log.info("[00_runtime] 已加载；执行 lua_rt 运行冒烟测试")
