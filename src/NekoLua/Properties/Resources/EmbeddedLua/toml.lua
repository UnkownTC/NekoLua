local toml = {}

local function splitLines(text)
    text = text:gsub("\r\n", "\n")
    local lines = {}
    for line in (text .. "\n"):gmatch("([^\n]*)\n") do
        lines[#lines + 1] = line
    end
    return lines
end

local function stripComment(line)
    local inSingle = false
    local inDouble = false

    for i = 1, #line do
        local c = line:sub(i, i)
        if c == "'" and not inDouble then
            inSingle = not inSingle
        elseif c == '"' and not inSingle then
            inDouble = not inDouble
        elseif c == "#" and not inSingle and not inDouble then
            return line:sub(1, i - 1)
        end
    end
    return line
end

local function trim(s)
    return (s:gsub("^%s+", ""):gsub("%s+$", ""))
end

local function unescape(s)
    return (s:gsub("\\(.)", function(c)
        if c == "n"  then return "\n" end
        if c == "t"  then return "\t" end
        if c == "r"  then return "\r" end
        if c == "\\" then return "\\" end
        if c == '"'  then return '"'  end
        if c == "'"  then return "'"  end
        if c == "b"  then return "\b" end
        if c == "f"  then return "\f" end
        return c
    end))
end

local function parseKey(s)
    s = trim(s)
    if s == "" then return nil end

    local first = s:sub(1, 1)
    if first == '"' then
        local inner = s:match('^"(.*)"$')
        return inner and unescape(inner) or nil
    elseif first == "'" then
        return s:match("^'(.*)'$")
    else
        if s:match("^[%w_%-]+$") then return s end
        return nil
    end
end

local function splitKeyPath(path)
    local parts = {}
    for seg in path:gmatch("[^%.]+") do
        local k = parseKey(seg)
        if not k then return nil, seg end
        parts[#parts + 1] = k
    end
    return parts
end

local parseValue

local function findClosing(s, openCh, closeCh, startPos)
    local depth = 0
    local inS   = false
    local inD   = false

    for i = startPos, #s do
        local c = s:sub(i, i)
        if c == "'" and not inD then inS = not inS
        elseif c == '"' and not inS then inD = not inD
        elseif not inS and not inD then
            if c == openCh then
                depth = depth + 1
            elseif c == closeCh then
                depth = depth - 1
                if depth == 0 then return i end
            end
        end
    end
    return nil
end

local function splitTopLevel(s, sep)
    local result = {}
    local buf = {}
    local depth = 0
    local inS = false
    local inD = false

    for i = 1, #s do
        local c = s:sub(i, i)
        if c == "'" and not inD then inS = not inS
        elseif c == '"' and not inS then inD = not inD
        elseif not inS and not inD then
            if c == "[" or c == "{" then
                depth = depth + 1
            elseif c == "]" or c == "}" then
                depth = depth - 1
            elseif c == sep and depth == 0 then
                result[#result + 1] = table.concat(buf)
                buf = {}
                goto continue
            end
        end
        buf[#buf + 1] = c
        ::continue::
    end

    if #buf > 0 then result[#result + 1] = table.concat(buf) end
    return result
end

local function parseArray(s)
    local close = findClosing(s, "[", "]", 1)
    if not close then error("toml: 数组未闭合", 0) end

    local inner = s:sub(2, close - 1)
    local arr = {}

    for _, item in ipairs(splitTopLevel(inner, ",")) do
        local v = trim(item)
        if v ~= "" then
            arr[#arr + 1] = parseValue(v)
        end
    end

    return arr
end

local function parseInlineTable(s)
    local close = findClosing(s, "{", "}", 1)
    if not close then error("toml: 内联表未闭合", 0) end

    local inner = s:sub(2, close - 1)
    local tbl = {}

    for _, pair in ipairs(splitTopLevel(inner, ",")) do
        local p = trim(pair)
        if p ~= "" then
            local eq = p:find("=")
            if not eq then error("toml: 内联表缺少 '='", 0) end

            local k = parseKey(p:sub(1, eq - 1))
            local v = parseValue(trim(p:sub(eq + 1)))

            if k then tbl[k] = v end
        end
    end

    return tbl
end

parseValue = function(s)
    s = trim(s)
    if s == "" then error("toml: 值为空", 0) end

    local first = s:sub(1, 1)

    if s:sub(1, 3) == '"""' then
        local close = s:find('"""', 4, true)
        if not close then error("toml: 多行字符串未闭合", 0) end
        local body = s:sub(4, close - 1)
        body = body:gsub("^\n", "")
        return unescape(body)
    end

    if s:sub(1, 3) == "'''" then
        local close = s:find("'''", 4, true)
        if not close then error("toml: 多行字面字符串未闭合", 0) end
        local body = s:sub(4, close - 1)
        body = body:gsub("^\n", "")
        return body
    end

    if first == '"' then
        local close = s:find('"', 2, true)
        if not close then error("toml: 字符串未闭合", 0) end
        return unescape(s:sub(2, close - 1))
    end

    if first == "'" then
        local close = s:find("'", 2, true)
        if not close then error("toml: 字面字符串未闭合", 0) end
        return s:sub(2, close - 1)
    end

    if first == "[" then
        return parseArray(s)
    end

    if first == "{" then
        return parseInlineTable(s)
    end

    if s == "true"  then return true  end
    if s == "false" then return false end

    local num = s:gsub("_", "")

    local hex = num:match("^0x([%x]+)$")
    if hex then return tonumber(hex, 16) end

    local oct = num:match("^0o([0-7]+)$")
    if oct then return tonumber(oct, 8) end

    local bin = num:match("^0b([01]+)$")
    if bin then return tonumber(bin, 2) end

    local n = tonumber(num)
    if n then return n end

    return s
end

function toml.parse(text)
    if type(text) ~= "string" then
        error("toml.parse: 参数需要字符串", 0)
    end

    local root = {}
    local current = root
    local lines = splitLines(text)
    local i = 1

    while i <= #lines do
        local line = stripComment(lines[i])
        local trimmed = trim(line)

        if trimmed == "" then
            i = i + 1
            goto continue
        end

        local isMultiArray =
            trimmed:find("^[%w_%-\"']+%s*=%s*%[") and
            not trimmed:find("%]%s*$")

        local isMultiBasic =
            trimmed:find('^[%w_%-\"\'%.]+%s*=%s*"""') and
            not trimmed:find('"""%s*$')

        local isMultiLiteral =
            trimmed:find("^[%w_%-\"\'%.]+%s*=%s*'''") and
            not trimmed:find("'''%s*$")

        if isMultiArray or isMultiBasic or isMultiLiteral then
            local closeToken = isMultiArray and "]" or (isMultiBasic and '"""' or "'''")

            local buf = { lines[i] }
            local j = i + 1

            while j <= #lines do
                buf[#buf + 1] = lines[j]
                local stripped = stripComment(lines[j])
                if stripped:find(closeToken, 1, true) then
                    break
                end
                j = j + 1
            end

            lines[i] = table.concat(buf, "\n")
            for k = j, i + 1, -1 do
                table.remove(lines, k)
            end

            line = stripComment(lines[i])
            trimmed = trim(line)
        end

        local arrPath = trimmed:match("^%[%[(.+)%]%]$")
        if arrPath then
            local parts, bad = splitKeyPath(trim(arrPath))
            if not parts then error("toml: 非法键 '" .. tostring(bad) .. "'", 0) end
            if #parts == 0 then error("toml: 空路径", 0) end

            local parent = root
            for i2 = 1, #parts - 1 do
                local k = parts[i2]
                if parent[k] == nil then parent[k] = {} end
                if type(parent[k]) ~= "table" then
                    error("toml: 路径 '" .. arrPath .. "' 与标量冲突", 0)
                end
                parent = parent[k]
            end

            local lastKey = parts[#parts]
            if parent[lastKey] == nil then parent[lastKey] = {} end
            if type(parent[lastKey]) ~= "table" then
                error("toml: '" .. arrPath .. "' 与标量冲突", 0)
            end

            local newEntry = {}
            parent[lastKey][#parent[lastKey] + 1] = newEntry
            current = newEntry

            i = i + 1
            goto continue
        end

        local secPath = trimmed:match("^%[(.+)%]$")
        if secPath then
            local parts, bad = splitKeyPath(trim(secPath))
            if not parts then error("toml: 非法键 '" .. tostring(bad) .. "'", 0) end

            current = root
            for i2 = 1, #parts do
                local k = parts[i2]
                if current[k] == nil then current[k] = {} end
                if type(current[k]) ~= "table" then
                    error("toml: 路径 '" .. secPath .. "' 与标量冲突", 0)
                end
                current = current[k]
            end

            i = i + 1
            goto continue
        end

        local eq = trimmed:find("=")
        if not eq then
            error("toml: 缺少 '=' -> " .. trimmed, 0)
        end

        local keyPart = trimmed:sub(1, eq - 1)
        local valPart = trimmed:sub(eq + 1)

        local keys, bad = splitKeyPath(trim(keyPart))
        if not keys then error("toml: 非法键 '" .. tostring(bad) .. "'", 0) end
        if #keys == 0 then error("toml: 空键", 0) end

        local target = current
        for i2 = 1, #keys - 1 do
            local k = keys[i2]
            if target[k] == nil then target[k] = {} end
            if type(target[k]) ~= "table" then
                error("toml: 路径 '" .. keyPart .. "' 与标量冲突", 0)
            end
            target = target[k]
        end

        target[keys[#keys]] = parseValue(valPart)

        i = i + 1
        ::continue::
    end

    return root
end

local function isArray(t)
    local count = 0
    local max = 0
    for k in pairs(t) do
        if type(k) ~= "number" then return false end
        if k < 1 or k ~= math.floor(k) then return false end
        if k > max then max = k end
        count = count + 1
    end
    return count > 0 and count == max
end

local function isTableArray(t)
    if not isArray(t) or #t == 0 then return false end
    for i = 1, #t do
        if type(t[i]) ~= "table" then return false end
        if isArray(t[i]) then return false end
    end
    return true
end

local function escapeString(s)
    s = s:gsub("\\", "\\\\")
    s = s:gsub('"', "\\\"")
    s = s:gsub("\n", "\\n")
    s = s:gsub("\r", "\\r")
    s = s:gsub("\t", "\\t")
    return s
end

local function writeValue(sb, v)
    local t = type(v)

    if t == "string" then
        sb[#sb + 1] = '"' .. escapeString(v) .. '"'

    elseif t == "number" then
        if v == math.floor(v) and v >= -2^53 and v <= 2^53 then
            sb[#sb + 1] = string.format("%d", v)
        else
            sb[#sb + 1] = string.format("%.17g", v)
        end

    elseif t == "boolean" then
        sb[#sb + 1] = v and "true" or "false"

    elseif t == "table" then
        if isArray(v) then
            sb[#sb + 1] = "["
            for i = 1, #v do
                if i > 1 then sb[#sb + 1] = ", " end
                writeValue(sb, v[i])
            end
            sb[#sb + 1] = "]"
        else
            sb[#sb + 1] = "{"
            local keys = {}
            for k in pairs(v) do keys[#keys + 1] = k end
            table.sort(keys, function(a, b) return tostring(a) < tostring(b) end)

            for i, k in ipairs(keys) do
                if i > 1 then sb[#sb + 1] = ", " end
                sb[#sb + 1] = tostring(k) .. " = "
                writeValue(sb, v[k])
            end
            sb[#sb + 1] = "}"
        end

    else
        sb[#sb + 1] = '""'
    end
end

local function writeKey(sb, k)
    if type(k) == "string" and k:match("^[%w_%-]+$") then
        sb[#sb + 1] = k
    else
        sb[#sb + 1] = '"' .. escapeString(tostring(k)) .. '"'
    end
end

local function writeTable(sb, t, prefix)
    local keys = {}
    for k in pairs(t) do keys[#keys + 1] = k end
    table.sort(keys, function(a, b) return tostring(a) < tostring(b) end)

    for _, k in ipairs(keys) do
        local v = t[k]
        if type(v) ~= "table" or isArray(v) then
            writeKey(sb, k)
            sb[#sb + 1] = " = "
            writeValue(sb, v)
            sb[#sb + 1] = "\n"
        end
    end

    for _, k in ipairs(keys) do
        local v = t[k]
        if type(v) == "table" and not isArray(v) then
            local fullKey = prefix == "" and tostring(k) or (prefix .. "." .. tostring(k))
            sb[#sb + 1] = "\n[" .. fullKey .. "]\n"
            writeTable(sb, v, fullKey)

        elseif type(v) == "table" and isTableArray(v) then
            local fullKey = prefix == "" and tostring(k) or (prefix .. "." .. tostring(k))
            for i = 1, #v do
                sb[#sb + 1] = "\n[[" .. fullKey .. "]]\n"
                writeTable(sb, v[i], fullKey)
            end
        end
    end
end

function toml.encode(t)
    if type(t) ~= "table" then
        error("toml.encode: 参数需要 table", 0)
    end

    local sb = {}
    writeTable(sb, t, "")
    return table.concat(sb)
end

function toml.isValid(text)
    if type(text) ~= "string" then return false end
    local ok = pcall(toml.parse, text)
    return ok
end

return toml