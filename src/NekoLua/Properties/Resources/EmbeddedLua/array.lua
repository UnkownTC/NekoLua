local array = {}

function array.map(t, fn)
    local r = {}
    for i = 1, #t do r[i] = fn(t[i], i) end
    return r
end

function array.filter(t, fn)
    local r = {}
    for i = 1, #t do
        if fn(t[i], i) then r[#r + 1] = t[i] end
    end
    return r
end

function array.reduce(t, fn, init)
    local acc = init
    for i = 1, #t do acc = fn(acc, t[i], i) end
    return acc
end

function array.each(t, fn)
    for i = 1, #t do fn(t[i], i) end
end

function array.find(t, fn)
    for i = 1, #t do
        if fn(t[i], i) then return t[i], i end
    end
    return nil
end

function array.findIndex(t, fn)
    for i = 1, #t do
        if fn(t[i], i) then return i end
    end
    return -1
end

function array.all(t, fn)
    for i = 1, #t do
        if not fn(t[i], i) then return false end
    end
    return true
end

function array.any(t, fn)
    for i = 1, #t do
        if fn(t[i], i) then return true end
    end
    return false
end

function array.count(t, fn)
    local n = 0
    for i = 1, #t do
        if fn(t[i], i) then n = n + 1 end
    end
    return n
end

function array.contains(t, v)
    for i = 1, #t do
        if t[i] == v then return true end
    end
    return false
end

function array.indexOf(t, v)
    for i = 1, #t do
        if t[i] == v then return i end
    end
    return -1
end

function array.lastIndexOf(t, v)
    for i = #t, 1, -1 do
        if t[i] == v then return i end
    end
    return -1
end

function array.first(t)
    return t[1]
end

function array.last(t)
    return t[#t]
end

function array.unique(t)
    local seen = {}
    local r = {}
    for i = 1, #t do
        local v = t[i]
        if not seen[v] then
            seen[v] = true
            r[#r + 1] = v
        end
    end
    return r
end

function array.flatten(t, depth)
    depth = depth or 1
    if depth < 1 then return array.clone(t) end

    local r = {}
    for i = 1, #t do
        local v = t[i]
        if type(v) == "table" then
            local sub = array.flatten(v, depth - 1)
            for j = 1, #sub do r[#r + 1] = sub[j] end
        else
            r[#r + 1] = v
        end
    end
    return r
end

function array.reverse(t)
    local r = {}
    local n = #t
    for i = 1, n do r[i] = t[n - i + 1] end
    return r
end

function array.slice(t, first, last)
    first = first or 1
    last  = last  or #t
    local r = {}
    for i = first, last do r[#r + 1] = t[i] end
    return r
end

function array.take(t, n)
    local r = {}
    for i = 1, math.min(n, #t) do r[i] = t[i] end
    return r
end

function array.skip(t, n)
    local r = {}
    for i = n + 1, #t do r[#r + 1] = t[i] end
    return r
end

function array.chunk(t, size)
    size = size or 1
    if size < 1 then size = 1 end

    local r = {}
    local cur = {}
    for i = 1, #t do
        cur[#cur + 1] = t[i]
        if #cur >= size then
            r[#r + 1] = cur
            cur = {}
        end
    end
    if #cur > 0 then r[#r + 1] = cur end
    return r
end

function array.clone(t)
    local r = {}
    for i = 1, #t do
        local v = t[i]
        r[i] = type(v) == "table" and array.clone(v) or v
    end
    return r
end

function array.concat(a, b)
    local r = {}
    for i = 1, #a do r[#r + 1] = a[i] end
    for i = 1, #b do r[#r + 1] = b[i] end
    return r
end

function array.sort(t, cmp)
    local r = array.clone(t)
    table.sort(r, cmp)
    return r
end

function array.sortBy(t, key)
    local r = array.clone(t)
    table.sort(r, function(a, b) return a[key] < b[key] end)
    return r
end

function array.sortByDesc(t, key)
    local r = array.clone(t)
    table.sort(r, function(a, b) return a[key] > b[key] end)
    return r
end

function array.groupBy(t, fn)
    local r = {}
    for i = 1, #t do
        local k = fn(t[i], i)
        r[k] = r[k] or {}
        r[k][#r[k] + 1] = t[i]
    end
    return r
end

function array.toMap(t, keyFn, valueFn)
    local r = {}
    for i = 1, #t do
        local k = keyFn(t[i], i)
        local v = valueFn and valueFn(t[i], i) or t[i]
        r[k] = v
    end
    return r
end

function array.zip(...)
    local arrays = {...}
    if #arrays == 0 then return {} end

    local n = #arrays[1]
    local r = {}
    for i = 1, n do
        local row = {}
        for j = 1, #arrays do row[j] = arrays[j][i] end
        r[i] = row
    end
    return r
end

function array.pairs(t)
    local r = {}
    for k, v in pairs(t) do
        r[#r + 1] = {k, v}
    end
    return r
end

function array.keys(t)
    local r = {}
    for k in pairs(t) do r[#r + 1] = k end
    return r
end

function array.values(t)
    local r = {}
    for _, v in pairs(t) do r[#r + 1] = v end
    return r
end

function array.range(a, b, step)
    step = step or 1

    if b == nil then
        b = a
        a = 1
    end

    local r = {}
    if step > 0 then
        for i = a, b, step do r[#r + 1] = i end
    elseif step < 0 then
        for i = a, b, step do r[#r + 1] = i end
    end
    return r
end

function array.repeat(v, n)
    local r = {}
    for i = 1, n do r[i] = v end
    return r
end

function array.fill(v, n)
    return array.repeat(v, n)
end

function array.sum(t)
    local s = 0
    for i = 1, #t do s = s + t[i] end
    return s
end

function array.min(t)
    if #t == 0 then return nil end
    local m = t[1]
    for i = 2, #t do if t[i] < m then m = t[i] end end
    return m
end

function array.max(t)
    if #t == 0 then return nil end
    local m = t[1]
    for i = 2, #t do if t[i] > m then m = t[i] end end
    return m
end

function array.mean(t)
    if #t == 0 then return 0 end
    return array.sum(t) / #t
end

function array.shuffle(t)
    local r = array.clone(t)
    for i = #r, 2, -1 do
        local j = math.random(i)
        r[i], r[j] = r[j], r[i]
    end
    return r
end

function array.sample(t, n)
    if n >= #t then return array.clone(t) end

    local r = array.clone(t)
    local out = {}
    for i = 1, n do
        local idx = math.random(#r)
        out[i] = table.remove(r, idx)
    end
    return out
end

function array.equals(a, b)
    if #a ~= #b then return false end
    for i = 1, #a do
        if a[i] ~= b[i] then return false end
    end
    return true
end

function array.join(t, sep)
    return table.concat(t, sep or "")
end

function array.removeIf(t, fn)
    local n = 0
    for i = #t, 1, -1 do
        if fn(t[i], i) then
            table.remove(t, i)
            n = n + 1
        end
    end
    return n
end

function array.dedupe(t)
    local seen = {}
    for i = #t, 1, -1 do
        if seen[t[i]] then
            table.remove(t, i)
        else
            seen[t[i]] = true
        end
    end
    return t
end

function array.push(t, ...)
    for i = 1, select("#", ...) do
        t[#t + 1] = select(i, ...)
    end
    return #t
end

function array.pop(t)
    return table.remove(t)
end

function array.shift(t)
    return table.remove(t, 1)
end

function array.unshift(t, v)
    table.insert(t, 1, v)
    return #t
end

return array