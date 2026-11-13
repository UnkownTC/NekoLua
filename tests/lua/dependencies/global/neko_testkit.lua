-- NekoLua 测试套件公共库（放到 <NekoLua>/dependencies/global/neko_testkit.lua）
--
-- 用法：
--   local kit = require("neko_testkit")
--   local s = kit.new("suite_name")
--   s:eq("label", actual, expected)
--   local ok, summary = s:finish()
--
-- 命令注册：
--   kit.command("lua_xxx", "说明", function(args, sender) ... end)

local M = {}

local function describe(value)
    if type(value) == "string" then
        return value
    end

    return tostring(value)
end

local function errorText(err)
    if type(err) == "table" then
        return err.message or err.trace or tostring(err)
    end

    return tostring(err)
end

-- 注意：这两个 helper 必须独立于 try.attempt 所在的帧。
-- MoonSharp 2.0.0 在「C# 回调内嵌套调用 Lua」之后，若原帧随后回写 local 会抛
-- AssignLocal NRE/IndexOutOfRange；因此 try.attempt 所在函数只做尾调用，不声明 local。
local function classifyRun(suite, label, ok, err)
    if ok then
        suite:pass(label)
    else
        suite:fail(label, errorText(err))
    end
end

local function classifyThrows(suite, label, needle, ok, err)
    if ok then
        suite:fail(label, "期望抛出异常，但成功返回")
        return
    end

    local text = errorText(err)
    if needle and string.find(text, needle, 1, true) == nil then
        suite:fail(label, "异常不含 '" .. needle .. "'：" .. text)
    else
        suite:pass(label)
    end
end

function M.new(name)
    local suite = {
        name = name,
        passed = 0,
        failed = 0,
        skipped = 0,
        lines = {},
    }

    function suite:record(level, message)
        local line = string.format("[%s] %s", self.name, message)
        table.insert(self.lines, line)

        if level == "error" then
            log.error(line)
        elseif level == "warn" then
            log.warn(line)
        else
            log.info(line)
        end
    end

    function suite:pass(label)
        self.passed = self.passed + 1
        self:record("info", "PASS " .. label)
    end

    function suite:fail(label, detail)
        self.failed = self.failed + 1
        self:record("error", "FAIL " .. label .. (detail and (" | " .. detail) or ""))
    end

    function suite:skip(label, reason)
        self.skipped = self.skipped + 1
        self:record("warn", "SKIP " .. label .. " | " .. (reason or ""))
    end

    function suite:eq(label, actual, expected)
        if actual == expected then
            self:pass(label)
        else
            self:fail(label, "期望 " .. describe(expected) .. "，实际 " .. describe(actual))
        end
    end

    function suite:truthy(label, value)
        if value then
            self:pass(label)
        else
            self:fail(label, "期望为真，实际 " .. describe(value))
        end
    end

    function suite:falsy(label, value)
        if not value then
            self:pass(label)
        else
            self:fail(label, "期望为假，实际 " .. describe(value))
        end
    end

    function suite:typeIs(label, value, expected)
        local actual = type(value)
        if actual == expected then
            self:pass(label)
        else
            self:fail(label, "期望类型 " .. expected .. "，实际 " .. actual)
        end
    end

    function suite:run(label, fn)
        -- 尾调用：本帧不声明 local，避开 MoonSharp 嵌套回调后的 AssignLocal 崩溃。
        return classifyRun(self, label, try.attempt(fn))
    end

    function suite:throws(label, fn, needle)
        return classifyThrows(self, label, needle, try.attempt(fn))
    end

    function suite:finish()
        local summary = string.format(
            "%s：通过 %d / 失败 %d / 跳过 %d",
            self.name, self.passed, self.failed, self.skipped)

        if self.failed > 0 then
            self:record("error", summary)
        else
            self:record("info", summary)
        end

        return self.failed == 0, summary
    end

    return suite
end

-- 注册一个控制台 + RA 双端命令。
-- 不在命令包装里使用 try.attempt：MoonSharp 2.0.0 在「C# 回调内再嵌套调用 Lua
-- 回调」之后回写 local 会触发 AssignLocal NRE；这里保持零 local，异常交给核心捕获。
function M.command(name, description, handler)
    local function invoke(args, sender)
        M.trace("command:" .. name)
        return true, tostring(handler(args, sender) or "OK")
    end

    local ok = command.register({
        name = name,
        desc = description,
        usage = { name },
        callback = invoke,
    }, { "console", "remoteAdmin" })

    return ok
end

function M.firstPlayer()
    local list = player.list()
    return #list > 0 and list[1] or nil
end

-- 追加一行到 mod 目录下的 trace.txt（用于定位 MoonSharp 崩溃点；file 表受路径守卫限制在 mod 目录内）。
function M.trace(label)
    if file ~= nil and file.append ~= nil then
        file.append("trace.txt", tostring(label) .. "\n")
    end
end

return M
