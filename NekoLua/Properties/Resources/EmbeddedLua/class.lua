local class = {}

local OPERATOR_METHODS = {
    "__add", "__sub", "__mul", "__div", "__mod", "__pow", "__unm",
    "__concat", "__len", "__eq", "__lt", "__le",
}

local OPERATOR_SET = {}
for _, m in ipairs(OPERATOR_METHODS) do
    OPERATOR_SET[m] = true
end

local function isTable(v)
    return type(v) == "table"
end

local function isFunc(v)
    return type(v) == "function"
end


local function collectOperators(cls, out)
    if not isTable(cls) then return end

    for _, p in ipairs(rawget(cls, "__parents") or {}) do
        collectOperators(p, out)
    end

    for k, v in pairs(cls) do
        if OPERATOR_SET[k] and isFunc(v) then
            out[k] = v
        end
    end
end

local function getInstanceMeta(cls)
    local meta = rawget(cls, "__instanceMeta")
    if meta ~= nil then return meta end

    meta = {
        __index = cls,
        __tostring = function(_)
            return tostring(rawget(cls, "__name")) .. " instance"
        end,
    }

    collectOperators(cls, meta)
    rawset(cls, "__instanceMeta", meta)
    return meta
end


local function newClass(name, ...)
    assert(type(name) == "string" and #name > 0,
        "class(name, ...): name 必须是非空字符串")

    local parents = {...}

    local cls = {
        __name    = name,
        __isClass = true,
        __parents = parents,
    }

    if #parents >= 1 then
        cls.__parent = parents[1]
    end

    local classMeta = {
        __call = function(c, ...)
            local meta = getInstanceMeta(c)

            local obj = setmetatable({}, meta)
            rawset(obj, "__class", c)

            local init = c.init
            if isFunc(init) then
                init(obj, ...)
            end

            return obj
        end,

        __index = function(t, key)
            local v = rawget(t, key)
            if v ~= nil then return v end

            for _, p in ipairs(rawget(t, "__parents") or {}) do
                if isTable(p) then
                    local pv = p[key]
                    if pv ~= nil then return pv end
                end
            end

            return nil
        end,

        __tostring = function(c)
            return "Class<" .. tostring(rawget(c, "__name")) .. ">"
        end,
    }

    setmetatable(cls, classMeta)
    return cls
end

function class.abstract(msg)
    msg = msg or "抽象方法未实现"
    return function()
        error(msg, 2)
    end
end

function class.isClass(t)
    return isTable(t) and rawget(t, "__isClass") == true
end

function class.isInstance(obj, cls)
    if not isTable(obj) then return false end

    local c = rawget(obj, "__class")
    while c do
        if c == cls then return true end
        c = rawget(c, "__parent")
    end

    return false
end

function class.super(cls, self, methodName, ...)
    local parent = rawget(cls, "__parent")
    assert(parent,
        "class.super: 类 '" .. tostring(rawget(cls, "__name")) .. "' 没有父类")

    local method = parent[methodName]
    assert(isFunc(method),
        "class.super: 父类没有方法 '" .. tostring(methodName) .. "'")

    return method(self, ...)
end

function class.getClass(obj)
    if not isTable(obj) then return nil end
    return rawget(obj, "__class")
end

function class.className(obj)
    local c = class.getClass(obj)
    return c and rawget(c, "__name") or nil
end

function class.isA(obj, nameOrClass)
    local c = class.getClass(obj)
    if not c then return false end

    local target = nameOrClass
    if type(nameOrClass) == "string" then
        while c do
            if rawget(c, "__name") == target then return true end
            c = rawget(c, "__parent")
        end
        return false
    end

    return class.isInstance(obj, target)
end

setmetatable(class, {
    __call = function(_, name, ...)
        return newClass(name, ...)
    end,
})

return class