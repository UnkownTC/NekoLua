# NekoLua Lua API 参考

> 适用版本：以仓库当前 `main` 为准；Astra 桥接基线 AstraFramework **4.0.0**，Lua 运行时 MoonSharp **2.0.0**。
> 本文只描述脚本可见面（Lua 全局表、对象成员与门控），C# 扩展契约见文末。
> 相关文档：[README](../README.md) · [运行流程](../src/NekoLua/FLOW.md) · [C# 扩展](EXTENSIONS.md) · [Astra 桥接设计](ASTRA-EXTENSION.md) · [Astra 桥接 API](API-ASTRA.md)

---

## 1. 运行模型

### 1.1 目录与入口

服务端 `SecretLab/NekoLua/` 下按端口隔离，`global` 子目录对所有端口生效：

```text
SecretLab/NekoLua/
  mods/<port>/<mod>/main.lua      端口专属 mod（入口固定为 main.lua）
  mods/global/<mod>/main.lua      全局 mod
  dependencies/<port>/ | global/  Lua 依赖（dofile/require 可见范围由路径守卫约束）
  configs/<port>/<mod>.json       mod.registercfg() 落盘位置
  extensions/*.dll                桥接扩展程序集（如 NekoLua.Astra.dll）
```

- 每个 mod 目录下必须有 `main.lua`，缺失时只告警并跳过。
- `require("a.b")` 解析为 mod 目录内的相对路径 `a/b.lua`；禁止越出 mod 目录与依赖目录。
- 脚本以 MoonSharp `Preset_HardSandbox` 执行：没有 `io` / `os` / `debug` / `package` / `load` / `loadfile` / `dofile`；需要的能力必须走代理表。

### 1.2 启动顺序

```text
MainPlugin.Enable()
  LuaEmbeddedExtractor.ExtractAll()      释放内置 Lua 依赖
  LuaRegister.Clear(); Register(core)    注册核心全局表
  LuaExtensionLoader.LoadAll()           扫描 extensions/*.dll，注册扩展表并调用 ILuaExtension.OnLoad
  LuaGameObject.Create()                 启动每帧驱动（timer/scheduler/async/main-thread 队列）
  LuaLoader.LoadAll()                    扫描 mods 目录并执行 main.lua
```

- 扩展在脚本执行**之前**完成注册与 `OnLoad`，因此 `main.lua` 一执行就能看到扩展表。
- `Disable()` 逆序调用 `ILuaExtension.OnUnload`，并清空 timer / eventhook / command / eventbus / network / scheduler / regex / win32 / 主线程队列。
- 扩展 DLL 用 `Assembly.Load(byte[])` 装载：运行期可以覆盖文件，但新代码要重启服务端才生效；扩展之间不能互相作为编译依赖。

### 1.3 表隔离与热重载

- 全局表不是共享单例：每个 mod 调用一次 `IRegisterLua.Create(mod)`，拿到属于自己的表实例；闭包中的状态天然按 mod 隔离。
- 同名表注册一律拒绝（忽略大小写），不会静默覆盖；冲突会打印 `Logger.Error`，先注册者生效。
- `nekolua reload` 会重建 mod 的 Script；`storage` 明确跨热重载保留，`timer` / `eventhook` / `command` / `eventbus` / `scheduler` 的注册会按 mod 回收。

## 2. 调用约定

### 2.1 表函数用点号，对象方法用冒号

```lua
timer.delay(1.0, function() end)        -- 表字段函数：点号
local e = elevator.list()[1]             -- 返回托管对象句柄
e:setDestination(1)                      -- 对象 public 方法：冒号
print(e.isReady)                         -- 对象 public 属性/字段：点号
```

`buffer.*`、`pool.*` 这类表同样是「字段函数」，不要写成 `buf:writeByte(65)`——冒号会把表自身当第一个实参。

### 2.2 对象句柄

- 游戏对象（`player` / `item` / `door` / `room` …）与 Astra 内容对象通过 `LuaObjectProxy` 暴露：
  - `obj.name` 读取 public 属性或字段，`obj.name = v` 写属性/字段；
  - `obj:method(a, b)` 调用 public 实例方法；
  - 成员名大小写不敏感；索引器与泛型方法不可见；
  - 方法内抛出的异常会转成 Lua 运行时错误，不会把 .NET 异常栈直接泄进脚本。
- 句柄身份由 `LuaObjectCache` 统一管理，同一托管对象在同一 mod 内拿到同一张表；对象被游戏销毁后旧句柄会失效。
- **守卫边界**：`LuaGlobalApiGuard` 只覆盖代理里显式登记的全服级入口；经由 `LuaObjectProxy` 反射出来的实例成员（例如 `door:destroy()`）不在门控内，由脚本审计与对象生命周期负责。

### 2.3 错误与异常

```lua
-- 会抛异常：catch 缺省时向上传播
try.run(function() error("boom") end, function(err) print(err.message) end)

-- 不抛异常：返回 (true, ...) 或 (false, errTable)
local ok, err = try.attempt(function() return risky() end)
if not ok then print(err.type, err.message, err.trace) end
```

- 代理层统一把参数错误抛成 `ScriptRuntimeException`，信息里带 API 签名，例如 `astra.role.give(ply, id): 需要一个非空字符串`。
- 第三方库抛出的 .NET 异常如果直接穿过 C# 回调边界，会破坏 MoonSharp 执行栈（表现为之后的任意 `local` 赋值崩溃）。桥接层已将 Astra 调用包进 `Guarded(...)`；自定义扩展也应在回调边界自行捕获并转成 `ScriptRuntimeException`。

### 2.4 线程与调度

- 脚本入口与每帧驱动都在主线程：`timer` / `scheduler` / `async` / `eventhook` 的回调默认在主线程执行。
- 网络等异步完成事件由 `LuaMainThread` 队列调度回主线程；队列上限 `MainThreadQueueLimit`，每帧最多处理 `MainThreadTasksPerTick` 条。
- Astra 桥接的写操作（`define` / `give` / `remove` / `wave.spawn` / `scheduler` 全部方法 / hint/audio/countdown/effect）强制校验主线程，非主线程调用会直接报错。

## 3. 全局表索引

共 **73** 张表。每张表都按 mod 实例化，下表为「表名 → 实现类」索引。


**核心运行时表**

| 表 | 实现 | 作用 |
|---|---|---|
| `async` | `LuaAsyncProxy` | async.run(fn) / async(fn)       -- 启动协程 |
| `bit` | `LuaBitProxy` | bit 表（32 位整数位运算） |
| `buffer` | `LuaBufferProxy` | buffer 表（大端序读写） |
| `crypto` | `LuaCryptoProxy` | crypto.md5 / sha1 / sha256 / sha512(str)     -> hex |
| `datetime` | `LuaDateTimeProxy` | datetime.now()                  -- Unix 秒（double） |
| `encoding` | `LuaEncodingProxy` | encoding.hex.encode / decode(str) |
| `env` | `LuaEnvProxy` | env 表（环境变量） |
| `eventbus` | `LuaEventBusProxy` | eventbus 表（跨 mod，payload 走 JSON） |
| `mathx` | `LuaMathxProxy` | 插值：lerp / invLerp / remap / clamp / clamp01 / smoothstep / smootherstep |
| `meta` | `LuaMetaProxy` | meta.type(x)                    -- "nil"/"number"/"string"/"table"/... |
| `mod` | `LuaModProxy` | mod.name                        -- 当前 mod 名 |
| `nekolua` | `LuaNekoLuaProxy` | nekolua.name / version / author / description |
| `pool` | `LuaPoolProxy` | local p = pool.new([capacity], factory) |
| `process` | `LuaProcessProxy` | process 表（由 Config.EnableProcess 控制，默认关闭） |
| `random` | `LuaRandomProxy` | random 表（基于 UnityEngine.Random） |
| `regex` | `LuaRegexProxy` | regex 表（.NET 正则，区别于 Lua 的模式匹配） |
| `scheduler` | `LuaSchedulerProxy` | scheduler.daily("08:00", [id], callback) |
| `storage` | `LuaStorageProxy` | storage 表（仅内存，跨热重载保留） |
| `template` | `LuaTemplateProxy` | template 表 —— 字符串模板替换（占位符语法 {{path}}） |
| `timer` | `LuaTimerProxy` | timer.delay(seconds[, id], callback)      -- 延时一次 |
| `timespan` | `LuaTimeSpanProxy` | timespan 表（秒为单位的时长工具） |
| `try` | `LuaTryCatchFinallyProxy` | try.run(tryFn[, catchFn[, finallyFn]]) |
| `uuid` | `LuaUuidProxy` | uuid.new()                      -- "xxxxxxxx-xxxx-..." |
| `version` | `LuaVersionProxy` | version.new(major[,minor[,build[,revision]]]) |

**游戏内容表**

| 表 | 实现 | 作用 |
|---|---|---|
| `broadcast` | `LuaBroadcastProxy` | broadcast 表（玩家屏幕广播） |
| `cassie` | `LuaCassieProxy` | cassie 表（对应 LabAPI `Announcer`） |
| `command` | `LuaCommandProxy` | command.register(name, desc, callback, targets[, options]) |
| `decontamination` | `LuaDecontaminationProxy` | 属性（反射自 Decontamination） |
| `door` | `LuaDoorProxy` | 静态查询 |
| `elevator` | `LuaElevatorProxy` | elevator.list()                 -- 所有电梯 |
| `eventhook` | `LuaEventHookProxy` | eventhook.bind(className, eventName, id, callback) |
| `hazard` | `LuaHazardProxy` | hazard 表（对应 LabAPI `Facility/Hazards/*`） |
| `item` | `LuaItemProxy` | 静态查找 |
| `labapi` | `LuaLabApiProxy` | labapi 表（LabAPI 宿主自身信息与只读工具面） |
| `log` | `LuaLogProxy` | log.info(msg) |
| `map` | `LuaMapProxy` | 属性 |
| `permissions` | `LuaPermissionsProxy` | permissions 表（对应 LabAPI `PermissionsManager` + `PermissionsExtensions`） |
| `pickup` | `LuaPickupProxy` | pickup 表（地上的掉落物） |
| `player` | `LuaPlayerProxy` | player.list() / readyList() / count() / readyCount() |
| `pocket` | `LuaPocketProxy` | pocket 表（对应 LabAPI `Facility/Rooms/PocketDimension/*`） |
| `ragdoll` | `LuaRagdollProxy` | 静态查找 |
| `reservedSlots` | `LuaReservedSlotsProxy` | reservedSlots 表（对称于 `whitelist`，对应 LabAPI `ReservedSlots`） |
| `respawn` | `LuaRespawnProxy` | respawn 表（对应 LabAPI `Facility/Respawning/*`） |
| `room` | `LuaRoomProxy` | room 表（对应 LabAPI `Room` / `LightsController` / `Camera`） |
| `round` | `LuaRoundProxy` | 属性（反射自 Round） |
| `scp914` | `LuaScp914Proxy` | scp914 表（对应 LabAPI `Scp914`，它同时是 `Room` 的子类） |
| `server` | `LuaServerProxy` | server 表（对应 LabAPI `Server`，**全静态面**） |
| `structure` | `LuaStructureProxy` | structure 表（对应 LabAPI `Facility/Structures/*` 全家） |
| `toy` | `LuaToyProxy` | toy 表 —— AdminToy 系统 |
| `warhead` | `LuaWarheadProxy` | 状态（只读） |
| `whitelist` | `LuaWhitelistProxy` | whitelist.filePath              -- 白名单文件路径（只读） |
| `window` | `LuaWindowProxy` | window.list() / count() |

**文件与 IO 表**

| 表 | 实现 | 作用 |
|---|---|---|
| `compression` | `LuaCompressionProxy` | compression 表（输入 text → UTF-8 → 压缩 → base64；反向同理） |
| `directory` | `LuaDirectoryProxy` | directory.create(path) |
| `file` | `LuaFileProxy` | file 表（相对路径以 mod 目录为基准，受 Config.BlockedPaths 限制） |
| `path` | `LuaPathProxy` | path 表（纯字符串操作，不触碰文件系统） |

**网络表**

| 表 | 实现 | 作用 |
|---|---|---|
| `ftp` | `LuaFtpProxy` | ftp 表（由 Config.EnableFtp 控制，默认关闭） |
| `http` | `LuaHttpProxy` | 同步（阻塞主线程） |
| `tcp` | `LuaTcpProxy` | tcp 表（由 Config.EnableNetwork 控制） |
| `udp` | `LuaUdpProxy` | udp 表（由 Config.EnableNetwork 控制） |
| `websocket` | `LuaWebSocketProxy` | websocket 表（由 Config.EnableNetwork 控制） |

**序列化表**

| 表 | 实现 | 作用 |
|---|---|---|
| `base64` | `LuaBase64Proxy` | base64.encode / decode(str)     -- 标准 base64 |
| `csv` | `LuaCsvProxy` | csv.decode(text[, hasHeader])   -> table |
| `json` | `LuaJsonProxy` | json.decode(str)                -> value |
| `xml` | `LuaXmlProxy` | xml.decode(str)                 -> table     属性用 "@name"，文本用 "#text" |
| `yaml` | `LuaYamlProxy` | yaml.decode(str)                -> table |

**原生互操作表**

| 表 | 实现 | 作用 |
|---|---|---|
| `intptr` | `LuaIntPtrProxy` | intptr 表 —— IntPtr 的 Lua 封装（table + 元表） |
| `win32` | `LuaWin32Proxy` | win32 表（由 Config.EnableWin32 控制，默认关闭） |

**Unity 数据表**

| 表 | 实现 | 作用 |
|---|---|---|
| `quaternion` | `LuaQuaternionProxy` | quaternion.new(x, y, z, w) / identity |
| `time` | `LuaTimeProxy` | time 表（Mirror 网络时间 + Unity 本地时间） |
| `vector2` | `LuaVector2Proxy` | vector2.new(x, y) |
| `vector3` | `LuaVector3Proxy` | vector3.new(x, y, z) |

**子库封装表**

| 表 | 实现 | 作用 |
|---|---|---|
| `nvorbis` | `LuaNvorbisProxy` | nvorbis 表（由 Config.EnableNvorbis 控制）—— OGG 音频读取 |

## 4. 核心运行时表

与游戏无关的基础设施：异步、定时器、事件、序列化、加解密、进程与原生互操作等。

### `async`

> 实现：`LuaAsyncProxy` · 源码：`src/NekoLua/Proxies/LuaAsyncProxy.cs`

```text
async 表：
  async.run(fn) / async(fn)       -- 启动协程
  async.wait(seconds)             -- 只能在协程里调，让出
  async.count()                   -- 本 mod 活跃协程数
```

### `bit`

> 实现：`LuaBitProxy` · 源码：`src/NekoLua/Proxies/LuaBitProxy.cs`

```text
bit 表（32 位整数位运算）：
  bit.band / bor / bxor(a, b, ...)     -- 折叠
  bit.bnot(a)
  bit.lshift / rshift / arshift / rol / ror(a, n)
  bit.tobit(a)
  bit.tohex(a[, n])                    -> 十六进制字符串
```

### `buffer`

> 实现：`LuaBufferProxy` · 源码：`src/NekoLua/Proxies/LuaBufferProxy.cs`

```text
buffer 表（大端序读写）：
  buffer.new() / fromHex(str) / fromBase64(str)

  b:writeByte / writeBytes(table) / writeInt16 / writeInt32 / writeInt64
     writeFloat / writeDouble / writeString
  b:readByte / readBytes(n) / readInt16 / readInt32 / readInt64
     readFloat / readDouble / readString(n)
  b:length() / hex() / base64() / bytes()
  b:clear() / reset()
  b.position（读写）/ b.size
```

### `crypto`

> 实现：`LuaCryptoProxy` · 源码：`src/NekoLua/Proxies/LuaCryptoProxy.cs`

```text
crypto 表：
  crypto.md5 / sha1 / sha256 / sha512(str)     -> hex
  crypto.hmacSha256(key, data)                 -> hex
  crypto.uuid()                                -- 标准 UUID v4
  crypto.randomBytes(n)                        -> hex（n: 1-1024）

  crypto.rsa.generate(bits)                    -> {public, private}（XML 字符串）
  crypto.rsa.encrypt(publicKey, text)          -> base64
  crypto.rsa.decrypt(privateKey, base64)       -> text
  crypto.rsa.sign(privateKey, text)            -> base64（SHA-256 签名）
  crypto.rsa.verify(publicKey, text, sig)      -> bool

  crypto.aes.generateKey(bits)                 -- bits: 128/192/256
  crypto.aes.generateIV()
  crypto.aes.encrypt(key, text)                -- IV 自动前置
  crypto.aes.decrypt(key, base64)
  crypto.aes.encryptWithIV(key, ivBase64, text)
  crypto.aes.decryptWithIV(key, ivBase64, base64)
```

### `datetime`

> 实现：`LuaDateTimeProxy` · 源码：`src/NekoLua/Proxies/LuaDateTimeProxy.cs`

```text
datetime 表：
  datetime.now()                  -- Unix 秒（double）
  datetime.millis()               -- Unix 毫秒
  datetime.format(fmt[, t])       -- strftime，"!" 前缀 = UTC
  datetime.parse(str, fmt)        -> Unix 秒
  datetime.parts([t])             -> {year, month, day, hour, min, sec, wday, yday}
  datetime.diff(a, b)             -- a - b
  datetime.utcOffset()            -- 本地时区偏移（秒）
```

### `encoding`

> 实现：`LuaEncodingProxy` · 源码：`src/NekoLua/Proxies/LuaEncodingProxy.cs`

```text
encoding 表：
  encoding.hex.encode / decode(str)
  encoding.url.encode / decode(str)
  encoding.html.escape / unescape(str)
  encoding.utf8.length(str)             -- 字节数
  encoding.utf8.char(codepoint)         -- 单字符
  encoding.utf8.codepoint(str[, i])     -- 第 i 个字符的 codepoint（1-based）
  encoding.utf8.bytes(str)              -- 字节值数组
```

### `env`

> 实现：`LuaEnvProxy` · 源码：`src/NekoLua/Proxies/LuaEnvProxy.cs`

```text
env 表（环境变量）：

  env.get(name)               -> string | nil
  env.has(name)               -> bool
  env.expand(str)             -> string    展开 %VAR%（Win）/ $VAR（Linux）
  env.list()                  -> table     {name = value}

  env.set(name, value)        -> bool      受 Config.EnvWriteEnabled 控制
  env.delete(name)            -> bool      同上
```

### `eventbus`

> 实现：`LuaEventBusProxy` · 源码：`src/NekoLua/Proxies/LuaEventBusProxy.cs`

```text
eventbus 表（跨 mod，payload 走 JSON）：
  eventbus.emit(topic, payload)   -> number 送达数
  eventbus.on(topic, id, callback)          -- callback(payload, sender)
  eventbus.once(topic, id, callback)
  eventbus.off(topic, id)         -> number
  eventbus.offAll()               -> number
  eventbus.count() / list()
```

### `mathx`

> 实现：`LuaMathxProxy` · 源码：`src/NekoLua/Proxies/LuaMathxProxy.cs`

```text
mathx 表：
  插值：lerp / invLerp / remap / clamp / clamp01 / smoothstep / smootherstep
  缓动：easeInQuad / easeOutQuad / easeInOutQuad / easeInCubic /
        easeOutCubic / easeInOutCubic / easeInSine / easeOutSine /
        easeInOutSine / easeOutElastic
  距离：distance2D / distance3D / manhattan / chebyshev
  统计：sum / mean / min / max / median / variance / stdev
  角度：deg / rad / wrapAngle / deltaAngle
  其他：sign / fract / repeat / pingPong
```

### `meta`

> 实现：`LuaMetaProxy` · 源码：`src/NekoLua/Proxies/LuaMetaProxy.cs`

```text
meta 表：
  meta.type(x)                    -- "nil"/"number"/"string"/"table"/...
  meta.typename(x)                -- 同 type
  meta.fields(t)                  -> table     键列表
  meta.hasField(t, k)             -> bool
  meta.size(t)                    -> number
  meta.isEmpty(t)                 -> bool
  meta.deepCopy(x)                -- JSON 往返深拷贝
  meta.equals(a, b)               -> bool      JSON 深度比较
```

### `mod`

> 实现：`LuaModProxy` · 源码：`src/NekoLua/Proxies/LuaModProxy.cs`

```text
mod 表：
  mod.name                        -- 当前 mod 名
  mod.config                      -- 当前配置（table）
  mod.registercfg(value)          -- 注册默认配置（table 或 json 字符串），
                                     与磁盘深合并后落盘
```

### `nekolua`

> 实现：`LuaNekoLuaProxy` · 源码：`src/NekoLua/Proxies/LuaNekoLuaProxy.cs`

```text
nekolua 表：
  nekolua.name / version / author / description
  nekolua.config                  -- 配置快照（反射自动扫描 Config 所有字段，转 camelCase）
```

### `pool`

> 实现：`LuaPoolProxy` · 源码：`src/NekoLua/Proxies/LuaPoolProxy.cs`

```text
pool 表：
  local p = pool.new([capacity], factory)
  p:get()                         -- 取（空则调 factory）
  p:release(obj)                  -- 放回
  p:size() / p:clear()
```

### `process`

> 实现：`LuaProcessProxy` · 源码：`src/NekoLua/Proxies/LuaProcessProxy.cs`

```text
process 表（由 Config.EnableProcess 控制，默认关闭）：

  process.run(cmd[, args[, callback]])  -> pid
    异步启动，callback(exit, stdout, stderr) 在主线程执行

  process.runSync(cmd[, args])          -> (exit, stdout, stderr)
    同步阻塞，会卡住主线程，慎用

  process.start(cmd[, args])            -> pid
    后台启动，不捕获输出

  process.kill(pid)                     -> bool
    受 Config.ProcessKillBlacklist 和
    Config.ProcessKillSelfForbidden 保护

  process.exists(pid)                   -> bool

args 是字符串数组（table），不是 shell 命令串
白名单：Config.ProcessWhitelist（空=允许所有）
```

### `random`

> 实现：`LuaRandomProxy` · 源码：`src/NekoLua/Proxies/LuaRandomProxy.cs`

```text
random 表（基于 UnityEngine.Random）：
  random.int(min, max)            -- 闭区间
  random.float(min, max)
  random.value                    -- [0, 1)
  random.pick(table)              -> 元素 | nil
```

### `regex`

> 实现：`LuaRegexProxy` · 源码：`src/NekoLua/Proxies/LuaRegexProxy.cs`

```text
regex 表（.NET 正则，区别于 Lua 的模式匹配）：

  regex.isMatch(str, pattern)         -> bool
  regex.match(str, pattern)           -> table | nil
    {value, index(1-based), length, groups}
    groups 支持数字索引（含 0=整体）和命名组
  regex.matchAll(str, pattern)        -> table   全部匹配
  regex.findAll(str, pattern)         -> table   只返回匹配字符串
  regex.replace(str, pattern, repl)   -> string  支持 $1 / ${name}
  regex.split(str, pattern)           -> table
  regex.escape(str)                   -> string  转义正则特殊字符
  regex.isValid(pattern)              -> bool
  regex.cacheCount()                  -> number  当前 mod 缓存的正则数

每个 mod 独立 LRU 缓存（上限 256），Reload / Disable 时自动清理。
```

### `scheduler`

> 实现：`LuaSchedulerProxy` · 源码：`src/NekoLua/Proxies/LuaSchedulerProxy.cs`

```text
scheduler 表：
  scheduler.daily("08:00", [id], callback)
  scheduler.weekly("monday", "09:00", [id], callback)
  scheduler.at("2026-10-01 12:00:00", [id], callback)
  scheduler.cancel(id) / cancelAll() / count()
```

### `storage`

> 实现：`LuaStorageProxy` · 源码：`src/NekoLua/Proxies/LuaStorageProxy.cs`

```text
storage 表（仅内存，跨热重载保留）：
  storage.set(key, value)
  storage.get(key)                -> value | nil
  storage.has(key)                -> bool
  storage.delete(key)             -> bool
  storage.clear()
  storage.keys()                  -> table
  storage.count()                 -> number
```

### `template`

> 实现：`LuaTemplateProxy` · 源码：`src/NekoLua/Proxies/LuaTemplateProxy.cs`

```text
template 表 —— 字符串模板替换（占位符语法 {{path}}）：

  template.render(str, vars)      -> string
    一次性渲染
    vars 支持点路径：{{user.profile.name}}
    找不到的 key 保留原文 {{xxx}}

  template.compile(str)           -> function
    编译成可复用函数：(vars) -> string
```

### `timer`

> 实现：`LuaTimerProxy` · 源码：`src/NekoLua/Proxies/LuaTimerProxy.cs`

```text
timer 表：
  timer.delay(seconds[, id], callback)      -- 延时一次
  timer.every(seconds[, id], callback)      -- 先触发再等待
  timer.everyLate(seconds[, id], callback)  -- 先等待再触发
  timer.cancel(id) / cancelAll() / count()
```

### `timespan`

> 实现：`LuaTimeSpanProxy` · 源码：`src/NekoLua/Proxies/LuaTimeSpanProxy.cs`

```text
timespan 表（秒为单位的时长工具）：
  构造：fromMilliseconds / fromSeconds / fromMinutes / fromHours / fromDays
        of({days=,hours=,minutes=,seconds=,milliseconds=})
  换算：seconds / milliseconds / minutes / hours / days / parts
  格式：format(ts[, fmt])
  运算：add / sub / mul / div / neg / abs
  判断：zero / isZero / compare / clamp
```

### `try`

> 实现：`LuaTryCatchFinallyProxy` · 源码：`src/NekoLua/Proxies/LuaTryCatchFinallyProxy.cs`

```text
try 表：
  try.run(tryFn[, catchFn[, finallyFn]])
    -- 返回 (true, tryFn 或 catchFn 的返回值...)
    -- catch 缺失时异常向上抛；finally 抛异常覆盖一切
  try.attempt(fn)
    -- 不抛异常版：返回 (true, ...) 或 (false, errTable)

errTable = { message, trace, type }
  type: "syntax" / "runtime" / "interpreter" / "unknown"
```

### `uuid`

> 实现：`LuaUuidProxy` · 源码：`src/NekoLua/Proxies/LuaUuidProxy.cs`

```text
uuid 表：
  uuid.new()                      -- "xxxxxxxx-xxxx-..."
  uuid.newShort()                 -- 无连字符
  uuid.isValid(str)               -> bool
  uuid.parse(str)                 -> table | nil
  uuid.nil                        -- 全零 UUID
```

### `version`

> 实现：`LuaVersionProxy` · 源码：`src/NekoLua/Proxies/LuaVersionProxy.cs`

```text
version 表：
  version.new(major[,minor[,build[,revision]]])
  version.parse(str)              -> table | nil
  version.zero
  version.tostring(v[, fields])   -- fields: 1/2/3/4
  version.major / minor / build / revision(v)
  version.compare(a, b)           -> -1 / 0 / 1
  version.equals / greater / less / greaterOrEqual / lessOrEqual(a, b)
  version.isAtLeast(v, major[,minor[,build[,revision]]])
  version.inRange(v, min, max)
```

## 5. 游戏内容表

玩家、物品、门、房间、回合、广播等游戏对象与全服能力；多数入口受 `LuaGlobalApiGuard` 标签门控。

### `broadcast`

> 实现：`LuaBroadcastProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaBroadcastProxy.cs`

```text
broadcast 表（玩家屏幕广播）：

  broadcast.send(message, duration[, flags[, clearPrevious]])
    flags: "normal" / "truncated" / "adminChat"
           支持 "|" 组合，如 "truncated|adminChat"
           也可以直接传数字
    clearPrevious: 默认 false，true 时先清空屏幕

  broadcast.normal / truncated / adminChat(message, duration[, clearPrevious])
    便捷形式，直接指定 flags

  broadcast.clear()               清空所有玩家屏幕上的广播

  broadcast.flags                 -- 常量表
    .normal = 0 / .truncated = 1 / .adminChat = 2
```

### `cassie`

> 实现：`LuaCassieProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaCassieProxy.cs`

```text
cassie 表（对应 LabAPI `Announcer`）：

  只读属性（反射自 Announcer）：
    cassie.isSpeaking                bool
    cassie.lineDatabase              object | nil
    cassie.allLines                  table   CassieLine 代理对象数组
    cassie.collectionNames           table   台词集合名

  查询（无副作用）：
    cassie.isValid(word)                    -> bool
    cassie.calculateDuration(msg[, speed])  -> number
    cassie.convertTeam(team, unitName)      -> string
    cassie.convertNumber(n)                 -> string
    cassie.convertScp(role)                 -> withoutSpace, withSpace

  播报（受 `Config.DeniedGlobalApis` 的 `cassie` 标签门控）：
    cassie.message(text [, subtitles [, playBackground [, priority [, glitchScale]]]])
    cassie.glitchyMessage(text, glitchChance, jamChance)
    cassie.clear()

说明：
  1. LabAPI 的新门面 `LabApi.Features.Wrappers.Cassie` 整体标记为
     `[Obsolete("Use Announcer instead of Cassie.", error: true)]`，
     因此本表以 **Announcer** 为权威实现，表名沿用 NekoLua 既有的 `cassie`。
  2. `Announcer.CalculateDuration(string, bool, float)` 与旧版
     `Message(string, bool, bool, bool, string)` / `Message(CassieTtsPayload, float, float)`
     均被 LabAPI 标记为过时，静态绑定器自动跳过；
     `calculateDuration` 由宿主代为构造 `CassiePlaybackModifiers` 后转发。
  3. `ScpTermination(Player, DamageHandlerBase)` 需要 Lua 无法构造的伤害处理器对象，
     不暴露。
```

### `command`

> 实现：`LuaCommandProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaCommandProxy.cs`

```text
command 表：
  command.register(name, desc, callback, targets[, options])
  command.register({spec}, targets)
  command.register({parentSpec, childSpec, ...}, targets)   -- 父子命令

  targets：{"console", "remoteAdmin", "client"}
           简写 {"con", "ra", "cli"}

  options：{aliases = {...}, isHide = bool, usages = {...}}

  command.unregister(name)                    -> number 从所有平台
  command.unregisterConsole / unregisterRemoteAdmin / unregisterClient(name)
  command.unregisterAll()                     -> number
  command.count()                             -> number

回调签名：function(args, sender) -> success?, response?
  sender.player 在 RA/Client 命令里可用
```

### `decontamination`

> 实现：`LuaDecontaminationProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaDecontaminationProxy.cs`

```text
decontamination 表：

  属性（反射自 Decontamination）：
    decontamination.status          string    "none" / "disabled" / "forced"（写受门控）
    decontamination.isDecontaminating bool    只读
    decontamination.serverTime      number    只读
    decontamination.roundStartTime  number    只读
    decontamination.offset          number    写受门控
    decontamination.elevatorsText   string    写受门控

写操作受 `Config.DeniedGlobalApis` 的 `decontamination` 标签门控。
```

### `door`

> 实现：`LuaDoorProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaDoorProxy.cs`

```text
door 表：

  静态查询：
    door.list()                        -- 所有门
    door.count()                       -- 数量
    door.get(id)                       -- 按 DoorName 字符串或门实例 id 取单个门
    door.byZone(zone)                  -- 按 FacilityZone 枚举名筛选
    door.byRoom(nameTagPrefix)         -- 按 NameTag 前缀筛选

  子类集合（对应 LabAPI Doors 命名空间下的 8 个 Door 派生包装类）：
    door.breakableDoors() / bulkheadDoors() / checkpointDoors() / dummyDoors() /
    door.elevatorDoors() / gates() / nonInteractableDoors() / timed173Gates()

  枚举字典：
    door.lockReason                    -- DoorLockReason
    door.permissions                   -- DoorPermissionFlags
    door.names                         -- DoorName

  实例（走通用代理，成员反射自 Door 包装类）：
    属性/方法见 LabApi.Features.Wrappers.Door
```

### `elevator`

> 实现：`LuaElevatorProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaElevatorProxy.cs`

```text
elevator 表：
  elevator.list()                 -- 所有电梯
  elevator.count()                -- 数量
  elevator.byGroup(group)         -- 按分组筛选
  elevator.groups()               -- 所有分组名
  elevator.lockAll()              -- 锁定所有电梯门
  elevator.unlockAll()            -- 解锁所有电梯门

实例属性：
  group, isReady, goingUp,
  currentDestinationLevel, nextDestinationLevel,
  currentSequence, anyDoorLockedReason, allDoorsLockedReason,
  dynamicAdminLock, worldSpaceBounds,
  doors, rooms, currentDestination, nextDestination

实例方法：
  e:setDestination(level[, force])
  e:sendToNextFloor()
  e:lockAllDoors() / e:unlockAllDoors()
  e:interact(player)

枚举常量：
  elevator.lockReason.*           -- DoorLockReason 位值
```

### `eventhook`

> 实现：`LuaEventHookProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaEventHookProxy.cs`

```text
eventhook 表：
  eventhook.bind(className, eventName, id, callback)
  eventhook.unbind(id)            -> number
  eventhook.unbindAll()           -> number
  eventhook.count()               -> number
  eventhook.classes()             -> table     可用事件类
  eventhook.events(className)     -> table     类下所有事件名
```

### `hazard`

> 实现：`LuaHazardProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaHazardProxy.cs`

```text
hazard 表（对应 LabAPI `Facility/Hazards/*`）：

  集合：
    hazard.list()             -> table    全部环境危害
    hazard.count()            -> number
    hazard.teslas()           -> table    Tesla 电网
    hazard.tantrums()         -> table    恶心圈
    hazard.sinkholes()        -> table    天坑
    hazard.amnesticClouds()   -> table    记忆消除云

  生成（受 `Config.DeniedGlobalApis` 的 `hazard` 标签门控）：
    hazard.spawnTantrum(pos[, rot[, scale]])                          -> TantrumHazard
    hazard.spawnSinkhole(pos[, rot[, scale]])                         -> SinkholeHazard
    hazard.spawnAmnesticCloud(pos[, rot[, scale[, duration[, size[, owner]]]]])
                                                                      -> AmnesticCloudHazard
    hazard.destroyAll()                                               -> number 销毁数量

说明：`Tesla` 与 `Hazard` 是两个平行体系（Tesla 不代表 EnvironmentalHazard），
因此 `list()` 不含电网；实例成员（`tesla.trigger()` / `hazard.isInArea(player)`
/ `hazard.destroy()`）由 LuaObjectProxy 反射覆盖。
```

### `item`

> 实现：`LuaItemProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaItemProxy.cs`

```text
item 表：

  静态查找：
    item.list()                    -- 所有物品
    item.count()                   -- 数量
    item.get(serial)               -- 按序号查（ushort）
    item.getByType(typeName)       -- 按类型筛选
    item.getByCategory(category)   -- 按分类筛选
    item.equipped()                -- 已装备的
    item.ownedBy(player)           -- 指定玩家持有的

  枚举常量：
    item.types.*                   -- ItemType 全部值（小写字符串）
    item.categories.*              -- ItemCategory 全部值
    item.addReasons.*              -- ItemAddReason
    item.tierFlags.*               -- ItemTierFlags 位值（数字）

  实例属性（走通用代理）：
    i.type / i.category / i.tierFlags / i.serial /
    i.isEquipped / i.isDestroyed / i.isPrefab /
    i.canEquip / i.canHolster / i.canDrop /
    i.weight / i.currentOwner / i.addReason /
    i.gameObject / i.base

  实例方法：
    i:dropItem()                   -- 掉落，返回 pickup
    i:moveTo(player)               -- 转移到玩家
```

### `labapi`

> 实现：`LuaLabApiProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaLabApiProxy.cs`

```text
labapi 表（LabAPI 宿主自身信息与只读工具面）：

  标识：
    labapi.version                   string   LabAPI 当前版本
    labapi.versionCompiled           string   编译期 LabAPI 版本
    labapi.isInitialized             bool     LabAPI（PluginLoader）是否已初始化

  路径（只读字符串；读写仍受 LuaPathGuard 的 mod 作用域白名单约束）：
    labapi.paths.appData / secretLab / labApi / plugins / dependencies / configs
    labapi.paths.raConfigPath / gameplayConfigPath / sharingConfigPath /
                 mutesConfigPath / userIdBansPath / ipBansPath /
                 whitelistConfigPath / reservedSlotsConfigPath

  插件与依赖（只读）：
    labapi.plugins.count() / enabledCount() / list() / dependencies() / config()

  命令（只读）：
    labapi.commands.types() / count() / names()

  程序集（只读）：
    labapi.assemblies.loaded() / missing(name)

  枚举与常量：
    labapi.enums.commandType / playerSearchFlags / scp079PingType / doorName
    labapi.priority.highest / high / medium / low / lowest

  RoleTypeId 判定（role.*，对应 LabAPI `RoleExtensions`）：
    labapi.roles.isScp/idDead/isNtf/isChaos/isMilitary/isCivilian(role)   -> bool
    labapi.roles.fullName(role)                                          -> string

  事件与表统计：
    labapi.events.classCount() / eventCount() / boundCount() / byClass()
    labapi.apis()                    -- 当前已注册的全部 Lua 表名
```

### `log`

> 实现：`LuaLogProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaLogProxy.cs`

```text
log 表：
  log.info(msg)
  log.warn(msg)
  log.error(msg)
  log.debug(msg[, enabled])       -- enabled 默认 true，传 false 静默
```

### `map`

> 实现：`LuaMapProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaMapProxy.cs`

```text
map 表：

  属性：
    map.seed                    -- 地图种子
    map.rooms / doors / elevators / teslas / generators /
    pickups / ragdolls / cameras / roomLights
    map.defaultEscapeZone       -- Bounds
    map.escapeZones             -- table 数组

  随机：
    map.getRandomRoom([zone|zones])
    map.getRandomDoor([zone|zones[, includeBorders]])
    map.getRandomElevator() / getRandomTesla() / ...
    map.getRandomPickup() / getRandomRagdoll([zone|zones])
    map.getRandomCamera([zone|zones]) / getRandomLight([zone|zones])

  逃脱区：
    map.addEscapeZone(bounds)                       -- 受 map 标签门控
    map.removeEscapeZone(bounds)                    -- 受 map 标签门控

  灯光：
    map.turnOffLights([duration][, zone|zones])     -- 受 map 标签门控
    map.turnOnLights([zone|zones])                  -- 受 map 标签门控
    map.setColorOfLights(color[, zone|zones])        -- 受 map 标签门控
    map.resetColorOfLights([zone|zones])             -- 受 map 标签门控

  随机：
    map.getRandomLocker([zone|zones])
```

### `permissions`

> 实现：`LuaPermissionsProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaPermissionsProxy.cs`

```text
permissions 表（对应 LabAPI `PermissionsManager` + `PermissionsExtensions`）：

  查询（不改变服务器状态，不受门控）：
    permissions.get(player | id)                 -> table   权限字符串数组
    permissions.byProvider(player | id)          -> table   provider 类型名 -> 权限数组
    permissions.has(player | id, perm)           -> bool
    permissions.hasAny(player | id, ...perms)    -> bool
    permissions.hasAll(player | id, ...perms)    -> bool

  变更（受 `Config.DeniedGlobalApis` 的 `permissions` 标签门控）：
    permissions.add(player | id, ...perms)
    permissions.remove(player | id, ...perms)
    permissions.reloadAll()

注：权限提供者的注册/反注册需要 C# 泛型 + `new()` 约束，Lua 无法构造，
因此 `registerProvider` / `unregisterProvider` 刻意不暴露。
```

### `pickup`

> 实现：`LuaPickupProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaPickupProxy.cs`

```text
pickup 表（地上的掉落物）：

  静态查找：
    pickup.list()                          -- 所有掉落物
    pickup.count()                         -- 数量
    pickup.get(serial)                     -- 按序号查
    pickup.getByType(typeName)             -- 按类型筛选
    pickup.getByCategory(category)         -- 按分类筛选

  创建：
    pickup.create(type, position)
    pickup.create(type, position, rotation)
    pickup.create(type, position, rotation, scale)
    pickup.create(type, position, rotation, scale, networkSpawn)
      → 返回 pickup 代理 | nil

  实例属性（走通用代理）：
    p.type / p.category / p.tier / p.serial / p.weight / p.isLocked /
    p.isInUse / p.isSpawned / p.isDestroyed / p.isPrefab /
    p.position（读写）/ p.rotation（读写）/ p.lastOwner / p.room /
    p.gameObject / p.transform / p.base

  实例方法：
    p:spawn()                              -- 网络生成
    p:destroy()                            -- 销毁
```

### `player`

> 实现：`LuaPlayerProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaPlayerProxy.cs`

```text
player 表：
  player.list() / readyList() / count() / readyCount()
  player.npcList() / dummyList() / regularNpcList() / unauthenticatedList()
  player.getAll([flags])          -- 按 PlayerSearchFlags 组合筛选（字符串或位值）
  player.get(id)                  -- 按 networkId / userId / nickname 查
  player.getByNickname(name[, requireFullMatch])
  player.getByDisplayName(name[, requireFullMatch])
  player.tryGetPlayersByName(name) -> table
  player.random() / host()

实例代理（走 LuaObjectProxy）：
  nickname / health / maxHealth / position / rotation / role / team
  isAlive / isSCP / isHuman / inventory / currentItem
  及 Player 类的全部 public 成员
```

### `pocket`

> 实现：`LuaPocketProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaPocketProxy.cs`

```text
pocket 表（对应 LabAPI `Facility/Rooms/PocketDimension/*`）：

  集合与实例：
    pocket.instance()                            -> PocketDimension | nil
    pocket.teleports()                           -> table   全部传送点
    pocket.items()                               -> table   全部口袋物品
    pocket.recycleChances()                      -> table   float 数组
    pocket.minPocketItemTriggerDelay             number    只读
    pocket.maxPocketItemTriggerDelay             number    只读

  查询：
    pocket.isPlayerInside(player)                -> bool
    pocket.isPickupInside(pickup)                -> bool
    pocket.getCaughtPosition(player)             -> vec3
    pocket.getRarity(pickup | item)              -> number
    pocket.getExitPosesForZone(zone)             -> table    Pose 数组

  操作（受 `Config.DeniedGlobalApis` 的 `pocket` 标签门控）：
    pocket.forceInside(player) / forceExit(player) / forceKill(player)
    pocket.randomizeExits()
    pocket.addExitPosesForZone(zone, poses) / removeExitPosesForZone(zone, poses)
    pocket.removeAllExitPosesForZone(zone)

说明：`Pose` 是 Unity 结构体，Lua 侧以普通表传入传出（由 LuaTypeMapper /
LuaObjectProxy 的自动结构体打包处理）。
```

### `ragdoll`

> 实现：`LuaRagdollProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaRagdollProxy.cs`

```text
ragdoll 表：

  静态查找：
    ragdoll.list()                    -- 所有尸体
    ragdoll.count()                   -- 数量
    ragdoll.getByNickname(name)       -- 按昵称筛选
    ragdoll.getByRole(roleName)       -- 按角色筛选
    ragdoll.byPlayer(player)          -- 该玩家的尸体

  创建：
    ragdoll.spawnFromPlayer(player)              -- 从玩家位置生成
    ragdoll.spawn(role, position, rotation, nickname)  -- 手动生成

  批量：
    ragdoll.destroyAll()              -- 销毁全部

  实例属性（走通用代理）：
    r.nickname（读写）/ r.role（读写）/ r.serial / r.position（读写）/
    r.rotation（读写）/ r.scale（读写）/ r.isConsumed（读写）/
    r.isDestroyed / r.damageHandler / r.base

  实例方法：
    r:isRevivableBy(player)           -- 指定 SCP-049 能否复活
    r:destroy()                       -- 销毁
    r:freeze() / r:unFreeze()
```

### `reservedSlots`

> 实现：`LuaReservedSlotsProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaReservedSlotsProxy.cs`

```text
reservedSlots 表（对称于 `whitelist`，对应 LabAPI `ReservedSlots`）：
  reservedSlots.filePath              -- 预留位配置路径（只读）
  reservedSlots.list()                -- 所有预留位用户 ID
  reservedSlots.count()               -- 数量
  reservedSlots.hasReservedSlot(id | player)  -- 是否持有预留位
  reservedSlots.reload()              -- 从文件重载
  reservedSlots.add(id | player)      -- 添加
  reservedSlots.remove(id | player)   -- 移除，返回 bool

写操作受 `Config.DeniedGlobalApis` 的 `reservedSlots` 标签门控。
```

### `respawn`

> 实现：`LuaRespawnProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaRespawnProxy.cs`

```text
respawn 表（对应 LabAPI `Facility/Respawning/*`）：

  波次句柄（无波次时为 nil）：
    respawn.primaryMtfWave()     -> MtfWave | nil
    respawn.primaryChaosWave()   -> ChaosWave | nil
    respawn.miniMtfWave()        -> MiniMtfWave | nil
    respawn.miniChaosWave()      -> MiniChaosWave | nil
    respawn.get(waveOrBase)      -> RespawnWave | nil

实例成员（`wave.influence` / `wave.respawnTokens` / `wave.timeLeft` /
`wave.maxWaveSize` / `wave.initiateRespawn()` / `wave.instantRespawn()` /
`wave.playAnnouncement()` / `wave.tryGetCurrentMilestone()` 等）
由 LuaObjectProxy 反射覆盖。
```

### `room`

> 实现：`LuaRoomProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaRoomProxy.cs`

```text
room 表（对应 LabAPI `Room` / `LightsController` / `Camera`）：

  集合：
    room.list()          -> table    全部房间（Room 代理对象数组）
    room.count()         -> number
    room.lights()        -> table    全部灯光控制器
    room.cameras()       -> table    全部摄像头

  查找：
    room.get(identifier)          -- 按 RoomIdentifier（来自 room 代理的 base 字段）
    room.byName(name)             -- 按 RoomName 枚举（如 "LczClassDSpawn"）
    room.byShape(shape)           -- 按 RoomShape 枚举
    room.byZone(zone)             -- 按 FacilityZone 枚举（如 "Hcz"）
    room.atPosition(vec3)         -> Room | nil
    room.findPath(from, to)       -> table   房间寻路

  随机：
    room.random() / randomLight() / randomCamera()

说明：实例成员（`room.area` / `room.zone` / `lights.lightsEnabled` /
`lights.flickerLights(duration)` / `room.base` 等）由 LuaObjectProxy 反射覆盖。
```

### `round`

> 实现：`LuaRoundProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaRoundProxy.cs`

```text
round 表：

  属性（反射自 Round）：
    round.isRoundStarted / isRoundInProgress / isRoundEnded  bool  只读
    round.canRoundEnd               bool      只读
    round.keepRoundOnOne            bool      读写
    round.isLocked                  bool      读写
    round.isLobbyLocked             bool      读写
    round.extraTargets              number    读写
    round.scpTargetsAmount          number    只读
    round.totalDeaths               number    只读
    round.killedBySCPs              number    只读
    round.escapedClassD             number    只读
    round.escapedScientists         number    只读
    round.survivingSCPs             number    只读
    round.changedIntoZombies        number    只读
    round.duration                  number    只读（TimeSpan → 秒）

  方法：
    round.start()
    round.restart([fast[, override[, action]]])
      action: "restart" / "shutdown" / "donothing"
    round.restartSilently()
    round.end([force]) -> bool
```

### `scp914`

> 实现：`LuaScp914Proxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaScp914Proxy.cs`

```text
scp914 表（对应 LabAPI `Scp914`，它同时是 `Room` 的子类）：

  状态：
    scp914.instance()                          -> Scp914 | nil
    scp914.knobSetting                         string    读写（受门控）
    scp914.mode                                string    读写（受门控）
    scp914.isUpgrading                         bool      只读
    scp914.chamberSize                         vec3      只读
    scp914.knobChangeSequenceTime / upgradeSequenceTime / sequenceCooldown /
    scp914.doorCloseDelay / itemUpgradeDelay / doorOpenDelay       number 只读

  操作（受 `Config.DeniedGlobalApis` 的 `scp914` 标签门控）：
    scp914.interact(code[, player])
    scp914.playSound(sound)

  处理器（只读）：
    scp914.getItemProcessor(itemType)          -> processor | nil
    scp914.getAllItemProcessors()              -> table    itemType -> processor

说明：
  1. `knobSetting` 可写会直接改变全服 914 档位，因此写操作挂 `scp914` 门禁。
  2. `SetItemProcessor&lt;T&gt;` 系列是泛型方法，Lua 无法提供类型实参，
     **不暴露**；如需自定义处理器请在 C# 插件内注册。
  3. 实例成员（`scp914.gate` / `scp914.intakeDoor` / `scp914.doors` 等）
     由 LuaObjectProxy 反射覆盖。
```

### `server`

> 实现：`LuaServerProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaServerProxy.cs`

```text
server 表（对应 LabAPI `Server`，**全静态面**）：

  只读属性：
    server.ip / port / playerCount / reservedSlots / tps
    server.idleModeActive / idleModeAvailable / isTransparentlyModded
    server.shutdownState / host / defaultPermissionProvider

  可写属性（受 `Config.DeniedGlobalApis` 的 `server` 标签门控）：
    server.maxPlayers / maxTps / spawnProtectDuration / friendlyFire /
    server.pauseIdleMode / serverListName / playerListName / playerListNameRefreshRate

  查询（无副作用）：
    server.isPlayerBanned(value) / getAllBannedPlayers([banType])

  管理（受 `server` 标签门控）：
    server.banPlayer(player | id, reason, duration[, issuer[, nickname]])
    server.kickPlayer(player | id, reason[, issuer])
    server.banUserId(userId, reason, duration[, nickname])
    server.banIpAddress(ip, reason, duration[, nickname])
    server.unbanUserId(userId) / unbanIpAddress(ip)
    server.sendBroadcast(msg, duration[, flags[, clearPrevious]])
    server.sendBroadcast(player, msg, duration[, flags[, clearPrevious]])
    server.sendAdminChatMessage(msg[, isSilent])
    server.sendAdminChatMessage(players, msg[, isSilent])
    server.clearBroadcasts([player])

  生命周期（受 `server` 标签门控）：
    server.restart([redirectPort])
    server.shutdown([redirectPort])

  控制台命令（额外受 `Config.AllowScriptRunCommand` +
  `Config.RunCommandPrefixWhitelist` 双层门控，并写审计日志）：
    server.runCommand(cmd) -> string
```

### `structure`

> 实现：`LuaStructureProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaStructureProxy.cs`

```text
structure 表（对应 LabAPI `Facility/Structures/*` 全家）：

  通用：
    structure.list()                    -> table   全部 Structure
    structure.count()                   -> number
    structure.get(obj)                  -> Structure | nil

  分类集合（均实时枚举）：
    structure.lockers() / standardLockers() / rifleRackLockers() /
    structure.pedestalLockers() / largeLockers() / experimentalWeaponLockers() /
    structure.wallCabinets() / lockerChambers() /
    structure.workstations() / microPedestals() / windows() / generators()

说明：`Window` 的破坏/修复仍走既有的 `window` 表（保留兼容）；
实例成员（`locker.chambers` / `locker.fillChambers()` /
`window.health` 等）由 LuaObjectProxy 反射覆盖。
```

### `toy`

> 实现：`LuaToyProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaToyProxy.cs`

```text
toy 表 —— AdminToy 系统：

  静态：
    toy.list() / count() / byType(name) / findNear(pos, r) / destroyAll()

  子类型快捷列表：
    toy.primitives() / lights() / speakers() /
    toy.capybaras() / waypoints() / shootingTargets() / interactables()

  创建（返回实例代理）：
    toy.createSpeaker([position[, rotation[, scale]]])
    toy.createSpeaker(options)              -- options 模式，见下
    toy.createPrimitive / createLight / createWaypoint   同上

  createSpeaker 的 options：
    {
        position      = vector3,
        rotation      = quaternion,
        scale         = vector3,
        controllerId  = number,      -- 0~255，省略自动分配
        isSpatial     = bool,        -- 默认 false
        volume        = number,      -- 默认 1.0
        minDistance   = number,
        maxDistance   = number,
        onlyFor       = player,      -- 只对指定玩家播放（用 ValidPlayers 过滤）
    }

  实例（走通用代理）：
    通用：position / rotation / scale / parent / isDestroyed /
         movementSmoothing / isStatic / syncInterval / spawn() / destroy()
    SpeakerToy：play(samples[, queue[, loop]]) / pause() / resume() /
         skip(n) / stop() / isPlaying / isPaused / volume /
         minDistance / maxDistance / controllerId / isSpatial / isLooping
    其他子类型：见各自 C# 类定义
```

### `warhead`

> 实现：`LuaWarheadProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaWarheadProxy.cs`

```text
warhead 表：

  状态（只读）：
    warhead.exists                    bool
    warhead.isDetonated               bool
    warhead.isDetonationInProgress    bool
    warhead.scenarioType              string

  状态（读写）：
    warhead.leverStatus               bool
    warhead.isAuthorized              bool
    warhead.isLocked                  bool
    warhead.detonationTime            number   剩余秒数
    warhead.cooldownTime              number
    warhead.forceCountdownToggle      bool
    warhead.deadManSwitchRemaining    number
    warhead.deadManSwitchMaxTime      number

  方法：
    warhead.start([isAutomatic[, suppressSubtitles[, activator]]])
    warhead.stop([activator])
    warhead.detonate()
    warhead.openBlastDoors() / closeBlastDoors()
    warhead.shake()

  场景：
    warhead.startScenarios      -- table 数组
    warhead.resumeScenarios
    warhead.deadmanSwitchScenario
```

### `whitelist`

> 实现：`LuaWhitelistProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaWhitelistProxy.cs`

```text
whitelist 表：
  whitelist.filePath              -- 白名单文件路径（只读）
  whitelist.list()                -- 所有白名单用户 ID
  whitelist.count()               -- 数量
  whitelist.enabled               -- 读写，是否启用
  whitelist.isOnWhitelist(id)     -- 是否在白名单列表里
  whitelist.isWhitelisted(id)     -- 是否真正生效（考虑 enabled）
  whitelist.reload()              -- 从文件重载
  whitelist.add(id | player)      -- 加白名单
  whitelist.remove(id | player)   -- 移除，返回 bool
```

### `window`

> 实现：`LuaWindowProxy` · 源码：`src/NekoLua/Proxies/GameContents/LuaWindowProxy.cs`

```text
window 表：
  window.list() / count()
  window.broken() / intact()
  window.findNear(position, radius)
  window.damageAll(amount) / breakAll() / repairAll()
  window.repair(w)

实例：w.isBroken / isDestroyed / health（读写）
     position / rotation / base / transform / gameObject
```

## 6. 文件与 IO 表

受 `LuaPathGuard` 约束的路径、文件、目录与压缩包操作。

### `compression`

> 实现：`LuaCompressionProxy` · 源码：`src/NekoLua/Proxies/IO/LuaCompressionProxy.cs`

```text
compression 表（输入 text → UTF-8 → 压缩 → base64；反向同理）：
  compression.gzip / gunzip(text)
  compression.deflate / inflate(text)
```

### `directory`

> 实现：`LuaDirectoryProxy` · 源码：`src/NekoLua/Proxies/IO/LuaDirectoryProxy.cs`

```text
directory 表：
  directory.create(path)
  directory.delete(path)                  -- 递归
  directory.exists / empty / count(path)
  directory.list / listFiles / listDirectories(path)
  directory.info(path)            -> {name, path, created, modified}
  directory.move / copy(src, dst)
```

### `file`

> 实现：`LuaFileProxy` · 源码：`src/NekoLua/Proxies/IO/LuaFileProxy.cs`

```text
file 表（相对路径以 mod 目录为基准，受 Config.BlockedPaths 限制）：
  file.read(path)                 -> string | nil
  file.write(path, text)          -> bool
  file.append(path, text)         -> bool
  file.exists / delete(path)      -> bool
  file.size(path)                 -> number | nil
  file.name / parent(path)        -> string
  file.join(...)                  -> string
```

### `path`

> 实现：`LuaPathProxy` · 源码：`src/NekoLua/Proxies/IO/LuaPathProxy.cs`

```text
path 表（纯字符串操作，不触碰文件系统）：
  path.combine(...)
  path.full / normalize / extension / name / baseName / parent / root(str)
  path.changeExtension(str, newExt)
  path.isAbsolute / isRooted(str)     -> bool
  path.temp() / randomFile()          -> string
  path.separator / altSeparator       -- 主/备分隔符
```

## 7. 网络表

HTTP、TCP、UDP、WebSocket、FTP；受 `LuaNetworkGuard` 与配置开关约束。

### `ftp`

> 实现：`LuaFtpProxy` · 源码：`src/NekoLua/Proxies/Net/LuaFtpProxy.cs`

```text
ftp 表（由 Config.EnableFtp 控制，默认关闭）：

  ftp.connect(host [, port [, user [, pass]]])   -- 建立会话
  会话对象 c：
    c:list([path])            -> table     目录项名列表
    c:listDetails([path])     -> table     详细列表（含权限、大小）
    c:download(remote, local) -> bool
    c:upload(local, remote)   -> bool
    c:delete(remote)          -> bool
    c:rename(old, new)        -> bool
    c:mkdir(remote)           -> bool
    c:rmdir(remote)           -> bool
    c:size(remote)            -> number | nil
    c:close()

一次性连接（简写）：
  ftp.list(host, user, pass, path) -> table
  ftp.download(host, user, pass, remote, local) -> bool
  ...
```

### `http`

> 实现：`LuaHttpProxy` · 源码：`src/NekoLua/Proxies/Net/LuaHttpProxy.cs`

```text
http 表：
  同步（阻塞主线程）：
    http.get(url)                 -> (status, body, err)
    http.post / put / patch / delete(url, body)

  异步（回调在主线程执行）：
    http.asyncGet(url, callback)
    http.asyncPost / asyncPut / asyncPatch / asyncDelete(url, body, callback)
  callback(err, status, body)
```

### `tcp`

> 实现：`LuaTcpProxy` · 源码：`src/NekoLua/Proxies/Net/LuaTcpProxy.cs`

```text
tcp 表（由 Config.EnableNetwork 控制）：
  local c = tcp.create(host, port)      -- 异步连接，立即返回
  c.onConnect / onReceive / onError / onClose  -- 属性式回调
  c:send(text) / sendBytes(table)
  c:close()
  c.isOpen / c.isClosed
```

### `udp`

> 实现：`LuaUdpProxy` · 源码：`src/NekoLua/Proxies/Net/LuaUdpProxy.cs`

```text
udp 表（由 Config.EnableNetwork 控制）：
  local c = udp.create([bindPort])      -- bindPort 省略时随机
  c.onReceive = function(data, ip, port)
  c.onError = function(err)
  c:send(host, port, text) / sendBytes(host, port, table)
  c:close()
  c.isOpen / c.isClosed
```

### `websocket`

> 实现：`LuaWebSocketProxy` · 源码：`src/NekoLua/Proxies/Net/LuaWebSocketProxy.cs`

```text
websocket 表（由 Config.EnableNetwork 控制）：
  local ws = websocket.create(url)
  ws.onOpen / onMessage / onError / onClose
  ws:send(text) / close()
  ws.isOpen / ws.isClosed / ws.state    -- state: "open"/"closed"/...

注意：`ws:send()` 返回 `true` 仅表示**已进入发送队列**（不是"已送达对端"）。
真正发送失败会走 `ws.onError`。这样设计是因为 `ClientWebSocket` 不允许并发
`SendAsync`，而 Lua 侧是同步调用、不能阻塞服务端主线程等待发送完成。
```

## 8. 序列化表

base64 / csv / json / xml / yaml。

### `base64`

> 实现：`LuaBase64Proxy` · 源码：`src/NekoLua/Proxies/Serialization/LuaBase64Proxy.cs`

```text
base64 表：
  base64.encode / decode(str)     -- 标准 base64
  base64.urlEncode / urlDecode(str)  -- URL-safe（-_，无 padding）
  base64.isValid(str)             -> bool
```

### `csv`

> 实现：`LuaCsvProxy` · 源码：`src/NekoLua/Proxies/Serialization/LuaCsvProxy.cs`

```text
csv 表：
  csv.decode(text[, hasHeader])   -> table
    hasHeader=true（默认）：[{col=val}, ...]
    hasHeader=false      ：{{"a", "b"}, ...}
  csv.encode(table)               -> string
```

### `json`

> 实现：`LuaJsonProxy` · 源码：`src/NekoLua/Proxies/Serialization/LuaJsonProxy.cs`

```text
json 表：
  json.decode(str)                -> value
  json.encode(value)              -> string
  json.isValid(str)               -> bool
```

### `xml`

> 实现：`LuaXmlProxy` · 源码：`src/NekoLua/Proxies/Serialization/LuaXmlProxy.cs`

```text
xml 表：
  xml.decode(str)                 -> table     属性用 "@name"，文本用 "#text"
  xml.encode(value[, rootName])   -> string
  xml.isValid(str)                -> bool
```

### `yaml`

> 实现：`LuaYamlProxy` · 源码：`src/NekoLua/Proxies/Serialization/LuaYamlProxy.cs`

```text
yaml 表：
  yaml.decode(str)                -> table
  yaml.encode(value)              -> string
  yaml.isValid(str)               -> bool
```

## 9. 原生互操作表

Win32 与 IntPtr；需要 `Config.EnableWin32` 并受 DLL/函数白名单约束。

### `intptr`

> 实现：`LuaIntPtrProxy` · 源码：`src/NekoLua/Proxies/Interop/LuaPtrProxy.cs`

```text
intptr 表 —— IntPtr 的 Lua 封装（table + 元表）：

  构造：
    intptr.new(value)               -- 从 int64 构造
    intptr.parse("0x12345")         -- 从十六进制字符串解析
    intptr.null                     -- 空指针（值 = 0）
    intptr.zero                     -- 同 null

  实例字段/方法：
    p.value                         -- int64
    p.isNull                        -- bool
    p:add(n) -> ptr                 -- 加偏移
    p:sub(n) -> ptr                 -- 减偏移
    p:equals(other) -> bool         -- 相等比较
    p:toInt()  -> number            -- 同 value
    p:toHex()  -> string            -- "0x12345"
    p:toString() -> string          -- 同 toHex
    tostring(p)                     -- 通过 __tostring，同 toHex

  相等运算符：
    p1 == p2                        -- 通过 __eq，比较句柄值
```

### `win32`

> 实现：`LuaWin32Proxy` · 源码：`src/NekoLua/Proxies/Interop/LuaWin32Proxy.cs`

```text
win32 表（由 Config.EnableWin32 控制，默认关闭）：

  win32.triger(dll, func, args) -> number
    直接调用 Win32 函数
    args：数组 {a, b, c} 或带类型标注的 table
           {value = n, type = "int32"|"int64"|"uint"|"double"|"ptr"}
    返回 int32（BOOL/HRESULT 等语义由调用方决定）

  win32.bind(dll, func, aliasName [, callback])
    绑定 native 函数为全局名字：
      win32.bind("kernel32.dll", "Beep", "MyBeep")
      MyBeep(1000, 1000)

    若提供 callback，其**引用**若能在 Globals 中找到对应变量，
    该变量会被替换为转发函数（兼容用户示例）

  win32.unbind(aliasName) -> bool
    移除绑定

  win32.ptr(value) -> ptr
    从 int64 构造 IntPtr 封装

  win32.loaded() -> table     当前加载的 dll 列表
  win32.funcs()  -> table     当前缓存的函数名列表
  win32.free(dll)             释放 dll 引用

白名单：Config.Win32DllWhitelist / Win32FunctionWhitelist
```

## 10. Unity 数据表

向量、四元数与时间。

### `quaternion`

> 实现：`LuaQuaternionProxy` · 源码：`src/NekoLua/Proxies/UnityContents/LuaQuaternionProxy.cs`

```text
quaternion 表：
  quaternion.new(x, y, z, w) / identity
  构造：euler(x,y,z) / euler(vec3) / fromAngleAxis / lookRotation / fromToRotation
  运算：multiply / inverse
  插值：slerp / lerp
  查询：rotate(q,v) / angle / dot / eulerAngles
  分量：x / y / z / w(q)
```

### `time`

> 实现：`LuaTimeProxy` · 源码：`src/NekoLua/Proxies/UnityContents/LuaTimeProxy.cs`

```text
time 表（Mirror 网络时间 + Unity 本地时间）：

  time.now()      -> number    Mirror 网络时间（秒，double）
                               服务端/客户端一致，用于同步逻辑
                               不要转 float，会丢精度

  time.rtt()      -> number    往返延迟（秒）

  time.local()    -> number    本地已运行时间（秒，忽略 timeScale）

  time.delta()    -> number    上一帧间隔（秒，忽略 timeScale）
```

### `vector2`

> 实现：`LuaVector2Proxy` · 源码：`src/NekoLua/Proxies/UnityContents/LuaVector2Proxy.cs`

```text
vector2 表：
  vector2.new(x, y)
  常量：zero / one / up / down / left / right
  运算：add / sub / mul / div / neg
  几何：dot / length / lengthSqr / normalize / distance / angle / lerp
  分量：x(v) / y(v)
```

### `vector3`

> 实现：`LuaVector3Proxy` · 源码：`src/NekoLua/Proxies/UnityContents/LuaVector3Proxy.cs`

```text
vector3 表：
  vector3.new(x, y, z)
  常量：zero / one / up / down / left / right / forward / back
  运算：add / sub / mul / div / neg
  几何：dot / cross / length / lengthSqr / normalize / distance / angle / lerp
        clampMagnitude / project / reflect
  分量：x(v) / y(v) / z(v)
```

## 11. 子库封装表

对随服务端加载的第三方库的薄封装。

### `nvorbis`

> 实现：`LuaNvorbisProxy` · 源码：`src/NekoLua/Proxies/SubLibs/LuaNvorbisProxy.cs`

```text
nvorbis 表（由 Config.EnableNvorbis 控制）—— OGG 音频读取：

  元数据：
    nvorbis.info(path)                  -> table
      { sampleRate, channels, totalSamples, duration }

  一次性解码（浮点样本，交错 [-1, 1]）：
    nvorbis.read(path)                  -> table   全部
    nvorbis.read(path, maxSamples)      -> table   前 N 个
      上限由 Config.NvorbisMaxSamples 控制（默认 5000 万）

  流式回调（超大文件推荐，避免一次持有）：
    nvorbis.eachChunk(path, chunkSize, callback)
      callback(samples, index)          index 从 1 开始计数
      返回是否被中断（callback 返回 false 会提前终止）

  流式解码器：
    local s = nvorbis.open(path)
      s.sampleRate / channels / totalSamples / duration
      s:read(count)                     -> table
      s:seek(sampleIndex)               -> bool
      s:pos()                           -> number
      s:close()
```

## 12. RA / 控制台指令

父命令 `nekolua`，同时注册到 `RemoteAdminCommandHandler` 与 `GameConsoleCommandHandler`。

| 子命令 | 别名 | 说明 |
|---|---|---|
| `nekolua list` | `l`, `ls` | 列出已加载 mod、禁用状态与目录 |
| `nekolua reload [mod…]` | `r`, `rl` | 无参数重载全部启用 mod；指定名字只重载匹配项（忽略大小写） |
| `nekolua refresh` | `rf`, `scan` | 重新扫描 mods 目录，加载新增、卸载已删除 |

三个子命令都要求 `PlayerPermissions.ServerConsoleCommands`；异常详情只写服务端控制台，对发送者返回固定话术。

## 13. 配置项

配置文件由 LabAPI 加载，字段与默认值如下（`src/NekoLua/Config.cs`）。

| 字段 | 默认值 | 说明 |
|---|---|---|
| `ExtractEmbeddedLua` | `true` | 启动时释放内置 Lua 依赖 |
| `OverwriteEmbeddedLua` | `false` | 释放时覆盖已存在的同名文件 |
| `MainThreadQueueLimit` | `4096` | 主线程队列上限 |
| `MainThreadTasksPerTick` | `64` | 每帧最多处理的主线程任务数 |
| `HttpTimeoutSeconds` | `15` | HTTP 请求超时（秒） |
| `HttpMaxConcurrency` | `16` | 单次 HTTP 并发上限 |
| `EnableFtp` | `false` | 是否允许 `ftp` 表 |
| `BlockedPaths` | 系统目录默认表 | 路径守卫黑名单 |
| `AllowedPaths` | `[]` | 路径守卫白名单（优先于黑名单） |
| `EnvWriteEnabled` | `false` | 是否允许 `env.set` / `env.delete` |
| `TimerMaxConsecutiveErrors` | `5` | 定时器连续异常多少次后自动注销 |
| `EnableNetwork` | `true` | 网络总开关 |
| `BlockPrivateNetworkTargets` | `true` | 禁止访问内网/回环地址 |
| `MaxConnectionsPerMod` | `16` | 每个 mod 的连接数上限 |
| `EnableProcess` | `false` | 是否允许 `process` 表 |
| `ProcessWhitelist` | `[]` | 允许启动的进程名 |
| `ProcessKillBlacklist` | 系统关键进程 | 禁止结束的进程名 |
| `ProcessKillSelfForbidden` | `true` | 禁止结束服务端自身进程 |
| `EnableWin32` | `false` | 是否允许 `win32` 表 |
| `Win32DllWhitelist` | `kernel32`, `user32`, `shell32`, `gdi32`, `advapi32`, `ntdll` | 允许加载的 DLL |
| `Win32FunctionWhitelist` | `[]` | 非空时只允许列出的函数 |
| `Win32FunctionBlacklist` | 进程/内存/远程线程等危险函数 | 始终拒绝的函数（通配符匹配） |
| `Win32AllowPointerArgs` | `false` | 是否允许传指针参数 |
| `Win32IntPtrAsTable` | `true` | 返回值指针是否以可读表形式暴露 |
| `EnableNvorbis` | `true` | 是否允许 `nvorbis` 表 |
| `NvorbisMaxSamples` | `10000000` | 单文件解码采样上限 |
| `AllowScriptRunCommand` | `false` | 是否允许脚本执行服务端控制台命令 |
| `RunCommandPrefixWhitelist` | `[]` | 允许执行的命令前缀 |
| `DeniedGlobalApis` | `[]` | 关闭的全服级 API 标签，见 §14.3 |

## 14. C# 扩展 API

### 14.1 `IRegisterLua` — 新增一张全局表

```csharp
public interface IRegisterLua
{
    string Name { get; }                    // Lua 全局名，全局唯一（忽略大小写）
    Table Create(LuaModInstance mod);       // 每个 mod 调用一次，返回该 mod 专属表
}
```

- 放进 `SecretLab/NekoLua/extensions/` 的 DLL 会被反射扫描；类型必须是非抽象类、有公开无参构造函数。
- 也可以进程内直接调用 `LuaRegister.RegisterInstance(new MyTable())`；重名/空名返回 `false` 并记日志。
- 不要在 `Create` 里访问游戏对象：注册时服务端可能尚未就绪，把入口留到脚本调用或 `ILuaExtension.OnLoad` 之后。

### 14.2 `ILuaExtension` — 生命周期

```csharp
public interface ILuaExtension
{
    string Id { get; }   // 扩展标识，重复则后者被跳过
    void OnLoad();       // 主线程，LuaLoader.LoadAll 之前
    void OnUnload();     // 主线程，逆序调用
}
```

- 程序集无法卸载（net481 无 ALC），`OnLoad` 必须幂等，`OnUnload` 必须清干净静态状态。
- 扩展只应依赖 NekoLua 公开 API；把扩展当 LabAPI 插件注册会被 Astra 的自动扫描误收，桥接因此采用「普通 DLL + extensions 目录」形态。

### 14.3 全服级 API 门控

```csharp
LuaGlobalApiGuard.EnsureAllowed("warhead", "warhead.detonate()");
```

可用标签：`warhead` `round` `server` `whitelist` `reservedSlots` `permissions` `broadcast` `cassie` `decontamination` `map` `hazard` `pocket` `scp914`；Astra 桥接新增 `astra` `astra.role` `astra.team` `astra.wave`。

扩展在自己的全服级入口调用 `EnsureAllowed(tag, api)`；标签由运营者填进 `Config.DeniedGlobalApis`。

### 14.4 其它公开钩子

| API | 用途 |
|---|---|
| `LuaEventHook.RegisterEventClass(name, type)` | 给 `eventhook.bind` 增加一个事件类（类内 public 事件可被脚本绑定） |
| `LuaCleanupRegistry.Register(mod, action)` | 注册随 mod 重载/卸载执行的清理动作 |
| `LuaObjectCache.Wrap(obj)`（实例方法） | 复用托管对象缓存，避免 Lua GC 与 Unity 生命周期冲突 |
| `LuaPaths.Extensions` | `extensions/` 目录，扩展装载器扫描目标 |

## 15. 已知边界与版本基线

- 核心表由 `LuaRegister.Register(PluginAssembly)` 反射扫描；`extensions/` 的装载逐 DLL、逐类型隔离（加载/实例化/`OnLoad` 失败只记日志并继续），核心程序集自身的构造函数异常仍会中断启用。
- 同一个扩展 DLL 在一次启用中只会被装载一次；覆盖文件后必须重启才换新代码，且扩展之间不能互相引用。
- `LuaObjectProxy` 反射出的实例成员不受全服级门控覆盖，这是刻意的边界：新增全服破坏性能力时应加显式代理入口，而不是依赖反射。
- MoonSharp 2.0.0 对「尾调用 + 多返回值展开」有解析缺陷：`return f(a, try.attempt(fn))` 可能报 `attempt to call a string value`。把 `try.attempt` 的结果先落到 `local` 再使用。
- 本文的「全局表」章节由 `tools/gen-api-doc.ps1` 从源码 `<summary>` 生成；改代理注释后请重新生成并提交。

