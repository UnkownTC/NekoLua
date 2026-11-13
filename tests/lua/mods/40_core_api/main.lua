-- 40_core_api：NekoLua 核心表抽查（序列化 / 加密 / 文本 / 数学 / 工具）
-- 命令：lua_core（控制台 / RA）

local kit = require("neko_testkit")

kit.command("lua_core", "NekoLua 核心 API 抽查", function()
    local s = kit.new("40_core_api")

    -- base64
    local b64 = base64.encode("hello")
    s:eq("base64 往返", base64.decode(b64), "hello")
    s:truthy("base64.isValid", base64.isValid(b64))
    s:eq("base64.url 往返", base64.urlDecode(base64.urlEncode("a?b")), "a?b")

    -- csv
    s:typeIs("csv.encode", csv.encode({ { "a", "b" } }), "string")
    s:typeIs("csv.decode", csv.decode("a,b"), "table")

    -- regex
    s:truthy("regex.isMatch", regex.isMatch("abc123", "[0-9]+"))
    s:eq("regex.replace", regex.replace("a1b2", "[0-9]", "#"), "a#b#")
    s:truthy("regex.isValid", regex.isValid("[0-9]+"))
    s:eq("regex.escape", regex.escape("a.b"), "a\\.b")

    -- crypto
    s:eq("sha256 长度", #crypto.sha256("abc"), 64)
    s:eq("md5 长度", #crypto.md5("abc"), 32)
    s:truthy("crypto.uuid 合法", uuid.isValid(crypto.uuid()))

    -- buffer
    local buf = buffer.new()
    buf:writeByte(65)
    buf:writeInt32(123)
    buf:reset()
    s:eq("buffer readByte", buf:readByte(), 65)
    s:eq("buffer readInt32", buf:readInt32(), 123)
    s:truthy("buffer hex", #buf:hex() > 0)

    -- meta
    local source = { a = 1, nested = { b = 2 } }
    local copy = meta.deepCopy(source)
    copy.nested.b = 99
    s:eq("deepCopy 独立", source.nested.b, 2)
    s:truthy("meta.equals", meta.equals(source, { a = 1, nested = { b = 2 } }))
    s:eq("meta.size", meta.size(source), 2)
    s:truthy("meta.hasField", meta.hasField(source, "a"))

    -- encoding
    s:eq("hex.encode", encoding.hex.encode("A"), "41")
    s:eq("hex 往返", encoding.hex.decode("41"), "A")
    s:eq("url 往返", encoding.url.decode(encoding.url.encode("a b")), "a b")
    s:eq("utf8.length", encoding.utf8.length("abc"), 3)

    -- bit
    s:eq("bit.band", bit.band(12, 10), 8)
    s:eq("bit.lshift", bit.lshift(1, 4), 16)
    s:eq("bit.bor", bit.bor(1, 2, 4), 7)

    -- random
    s:eq("random.int 闭区间", random.int(5, 5), 5)
    s:eq("random.pick", random.pick({ 7 }), 7)

    -- datetime
    s:truthy("datetime.now", datetime.now() > 0)
    s:typeIs("datetime.parts", datetime.parts(), "table")
    s:truthy("datetime.format", type(datetime.format("%Y")) == "string")

    -- version
    local v = version.new(1, 2, 3)
    s:eq("version.major", version.major(v), 1)
    s:eq("version.compare", version.compare(version.new(1, 2), version.new(1, 3)), -1)
    s:truthy("version.isAtLeast", version.isAtLeast(v, 1, 2))

    -- uuid
    local id = uuid.new()
    s:truthy("uuid 合法", uuid.isValid(id))
    s:typeIs("uuid.parse", uuid.parse(id), "table")

    -- vector3
    local vec = vector3.new(3, 4, 0)
    s:eq("vector3.x", vector3.x(vec), 3)
    s:eq("vector3.length", vector3.length(vec), 5)
    s:eq("vector3.dot", vector3.dot(vec, vec), 25)

    -- mathx
    s:eq("mathx.lerp", mathx.lerp(0, 10, 0.5), 5)
    s:eq("mathx.clamp", mathx.clamp(5, 0, 3), 3)
    s:eq("mathx.sign", mathx.sign(-3), -1)

    -- template
    s:eq("template.render", template.render("hi {{name}}", { name = "neko" }), "hi neko")

    -- try
    local ok, err = try.attempt(function() error("boom") end)
    s:falsy("try.attempt 失败返回", ok)
    s:typeIs("try.attempt err", err, "table")

    -- pool
    local p = pool.new(2, function() return { n = 0 } end)
    local obj = p:get()
    p:release(obj)
    s:eq("pool.size", p:size(), 1)
    p:clear()

    local _, summary = s:finish()
    return summary
end)

log.info("[40_core_api] 已加载；执行 lua_core 运行核心 API 抽查")
