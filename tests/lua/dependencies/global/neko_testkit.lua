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

    local ok, text = pcall(tostring, value)
    return ok and text or "<unprintable>"
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
        local ok, err = pcall(fn)
        if ok then
            self:pass(label)
        else
            self:fail(label, describe(err))
        end
    end

    function suite:throws(label, fn, needle)
        local ok, err = pcall(fn)
        if ok then
            self:fail(label, "期望抛出异常，但成功返回")
            return
        end

        local text = describe(err)
        if needle and string.find(text, needle, 1, true) == nil then
            self:fail(label, "异常不含 '" .. needle .. "'：" .. text)
        else
            self:pass(label)
        end
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

-- 注册一个控制台 + RA 双端命令；handler 抛异常时返回失败给命令发送方。
function M.command(name, description, handler)
    local ok = command.register({
        name = name,
        desc = description,
        usage = { name },
        callback = function(args, sender)
            local okRun, result = pcall(handler, args, sender)
            if okRun then
                return true, tostring(result or "OK")
            end

            log.error(string.format("[%s] 测试命令异常: %s", name, tostring(result)))
            return false, tostring(result)
        end,
    }, { "console", "remoteAdmin" })

    return ok
end

function M.firstPlayer()
    local list = player.list()
    return #list > 0 and list[1] or nil
end

return M
