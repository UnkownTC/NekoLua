# NekoLua × AstraFramework 桥接 API

> 适用版本：AstraFramework **>= 3.3.1**，NekoLua 核心以仓库当前 `main` 为准。
> 桥接产物：`artifacts/build/NekoLua.Astra/x64/Release/net481/NekoLua.Astra.dll`，部署到 `SecretLab/NekoLua/extensions/`。
> 相关文档：[NekoLua API 参考](API.md) · [桥接设计（时序与决策）](ASTRA-EXTENSION.md) · [扩展开发](EXTENSIONS.md)

---

## 1. 部署与可用性

### 1.1 部署形态

| 项目 | 要求 |
|---|---|
| 运行环境 | 已加载 AstraFramework 插件（`Astra.Framework.dll` 由 Astra 自己发布） |
| 桥接位置 | `SecretLab/NekoLua/extensions/NekoLua.Astra.dll`，**不要**放 `plugins/` |
| 编译依赖 | `NekoLua.dll` + `Astra.Framework.dll`（`libs/astra/3.3.1/`）+ LabAPI / 游戏程序集 |
| 生效方式 | 覆盖 DLL 后重启服务端，或重新启用 NekoLua 插件 |

桥接是普通类库，不是 LabAPI 插件：Astra 的 `AutoRegister` 会扫描所有插件程序集，若桥接被当成插件，适配器类型会被误注册成模板。装载走 NekoLua 的 `extensions/` 扩展点（`Assembly.Load(byte[])`），详见 [EXTENSIONS.md](EXTENSIONS.md)。

### 1.2 可用性判定

```lua
if not astra.available then
    log.warn("astra 不可用：" .. astra.version)
    return
end
```

- `astra.available`：`Astra.Framework` 程序集版本是否 `>= 3.3.1`。
- `astra.version`：检测到的 Astra 版本字符串；读不到时为 `"unknown"`。
- 不可用时 `astra` 表仍然存在（脚本可以安全探测），但所有方法调用都会抛“需要 AstraFramework >= 3.3.1”。
- 不可用时桥接也**不会**注册 `astraplayer` 事件类与波次角色生成器。

### 1.3 装载时序

```text
NekoLua.MainPlugin.Enable()
  LuaExtensionLoader.LoadAll()
    NekoLua.Astra 的 ILuaExtension.OnLoad()
      读取 Astra 版本 → 判定 available
      注册事件类 "astraplayer"
      注册 OnServerPluginsEnabled 回调（用于 Flush）
      向 Astra 追加波次角色生成器（只接管本桥接注册的角色）
  LuaLoader.LoadAll()               ← main.lua 在这里执行
LabAPI OnServerPluginsEnabled
  AstraBridge.Flush()               ← 自动提交建好的内容
```

`main.lua` 里的 `astra.team.define` / `astra.role.define` / `astra.wave.define`：

1. 定义先进入桥接注册表（`Pending`）；
2. 若 Astra 已完成初始化（通常如此），立即尝试注册；
3. 否则由 `astra.flush()` 或下一帧的自动重试提交，最多重试 5 帧。

脚本**不需要**在每次 `define` 后手动 `flush`；保留 `flush()` 是为了诊断与极端加载顺序。

---

## 2. 快速开始

```lua
-- 1) 阵营
astra.team.define {
    id = "chaos_elite",
    name = "混沌精英",
    color = "#33AAFF",
    relation = "Custom",
    vanilla_team = "ChaosInsurgency",
    friendly = { "ChaosInsurgency" },
    hostile = { "NineTailedFox" },
}

-- 2) 角色
astra.role.define {
    id = "chaos_elite_rifleman",
    name = "精英步枪手",
    description = "混沌精英的突击单位",
    role_type = "ChaosRifleman",
    team = "chaos_elite",
    max_health = 150,
    items = { "Medkit", "GunAK" },
    waves = { "chaos_elite_wave" },
    wave_count = 2,
    on_added = function(ply) print("added: " .. ply.nickname) end,
    on_removed = function(ply) print("removed: " .. ply.nickname) end,
}

-- 3) 波次
astra.wave.define {
    id = "chaos_elite_wave",
    name = "混沌精英波次",
    teams = { "chaos_elite" },
    weight = 3,
    min_players = 8,
    table = { chaos_elite_rifleman = 2, ChaosRifleman = 1 },
    announcement = "混沌精英正在部署",
}

-- 4) 运行时
local wave = astra.wave.spawn("chaos_elite_wave")   -- 默认对全部玩家生成
for _, ply in ipairs(wave) do
    astra.hint.show(ply, "你被编入了精英波次", "CenterTop", 5)
end
```

---

## 3. 根表 `astra`

| 字段 / 方法 | 类型 | 说明 |
|---|---|---|
| `astra.available` | `boolean` | 版本满足要求时为 `true` |
| `astra.version` | `string` | Astra 程序集版本 |
| `astra.api_version` | `number` | 桥接 Lua API 版本，当前为 `1`；只增不改 |
| `astra.flush()` | `number` | 提交所有待注册内容，返回当前已注册内容条目数 |

- `flush()` 要求主线程，并受 `astra` 标签门控。
- 返回值是“当前注册表里已注册的条目总数”（诊断用），不是本次新注册数。

`astra` 下的分组：`team` / `role` / `wave` / `hint` / `audio` / `countdown` / `effect`。

### 3.1 门控与主线程

写操作统一走 `LuaGlobalApiGuard.EnsureAllowed(tag, api)`，运营者可在 `Config.DeniedGlobalApis` 中按标签关闭：

| 标签 | 覆盖 |
|---|---|
| `astra` | `flush` / `hint` / `audio` / `countdown` / `effect` |
| `astra.team` | `team.define` |
| `astra.role` | `role.define` / `give` / `remove` |
| `astra.wave` | `wave.define` / `spawn` / `scheduler` |

所有写操作、`flush`、`hint`、`audio`、`countdown`、`effect` 都要求主线程；`find` / `get` / `list` 等只读方法不强制校验，但仍建议在主线程使用（返回的是托管对象句柄）。

---

## 4. `astra.team`

### 4.1 方法

| 方法 | 返回 | 说明 |
|---|---|---|
| `astra.team.define(def)` | `string` | 定义或更新阵营，返回 `id`；失败抛异常 |
| `astra.team.find(id)` | `team` \| `nil` | 按 Id 查句柄 |
| `astra.team.list()` | `table` | 全部已注册阵营句柄数组 |

### 4.2 定义字段

| 字段 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `id` | `string` | 必填 | 全局唯一，≤ 64 字符，只允许字母、数字、`_`、`-` |
| `name` | `string` | 必填 | 显示名 |
| `color` | `string` | `"#FFFFFF"` | HTML 颜色；解析失败退回白色 |
| `relation` | `string` | `"Custom"` | `CustomTeamRelation` 枚举名 |
| `vanilla_team` | `string` | `"OtherAlive"` | 关联的原版 `Team` |
| `vanilla_faction` | `string` | `"Unclassified"` | 关联的原版 `Faction` |
| `keep_vanilla` | `boolean` | `false` | 是否沿用原版阵营关系 |
| `hostile` | `table` | `{}` | 敌对目标列表；元素是原版 Team/Faction 名或自定义阵营 Id |
| `friendly` | `table` | `{}` | 友好目标列表，规则同上 |

任一字段非法（枚举名错误、Id 超长、关系目标无法解析）会**整条拒绝**，不会留下半成品。

### 4.3 阵营句柄

`find` / `list` / `role` 返回的阵营是托管对象句柄，按 `LuaObjectProxy` 规则读取：

```lua
local team = astra.team.find("chaos_elite")
print(team.id, team.name, team.relation)
print(team.vanillaTeam)     -- 成员名大小写不敏感
```

常用成员：`id`、`name`、`teamColor`、`relation`、`keepVanillaRelate`、`vanillaTeam`、`vanillaFaction`、`blacklist`、`whitelist`。

---

## 5. `astra.role`

### 5.1 方法

| 方法 | 返回 | 说明 |
|---|---|---|
| `astra.role.define(def)` | `string` | 定义或更新角色，返回 `id` |
| `astra.role.give(ply, id)` | `boolean` | 给玩家套用自定义角色，成功返回 `true` |
| `astra.role.remove(ply)` | `true` | 移除玩家当前自定义角色 |
| `astra.role.get(ply)` | `role` \| `nil` | 玩家当前角色（仅自定义角色） |
| `astra.role.find(id)` | `role` \| `nil` | 按 Id 查角色模板 |
| `astra.role.list()` | `table` | 全部已注册角色模板 |

`ply` 接受玩家句柄、`userId` 字符串或 `networkId` 数字。

### 5.2 定义字段

| 字段 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `id` | `string` | 必填 | 全局唯一，≤ 64 字符，规则同阵营 |
| `name` | `string` | 必填 | 显示名 |
| `description` | `string` | `""` | 简介 |
| `role_type` | `string` | `"None"` | 原版 `RoleTypeId`，决定模型/语音等基础表现 |
| `team` | `string` | `""` | 自定义阵营 Id；空或找不到时使用 `role_type` 的默认阵营 |
| `max_health` | `number` | `100` | 最大生命值 |
| `items` | `table` | `{}` | 原版 `ItemType` 名或自定义物品 Id（≤ 64 字符） |
| `clear_inventory` | `boolean` | `true` | 生成时是否清空背包 |
| `show_custom_info` | `boolean` | `false` | 是否显示自定义角色信息 |
| `scale` | `table` | 原版默认 | `{x=, y=, z=}` 体型缩放 |
| `gravity` | `table` | 原版默认 | `{x=, y=, z=}` 重力 |
| `spawn_flags` | `string` | `"All"` | `RoleSpawnFlags` 枚举名 |
| `change_reason` | `string` | `"Respawn"` | `RoleChangeReason` 枚举名 |
| `waves` | `table` | `{}` | 角色所属波次 Id 列表 |
| `wave_count` | `number` | `1` | 每个波次生成数量；必须是正整数或 `-1`（填充剩余名额） |
| `wave_counts` | `table` | `nil` | `{ [waveId] = count }`，按波次覆盖 `wave_count` |
| `on_added` | `function` | `nil` | 角色添加回调 `function(ply)` |
| `on_removed` | `function` | `nil` | 角色移除回调 `function(ply)` |

`wave_count` 为 `0` 或小于 `-1` 会整条拒绝。`on_removed` 依赖 Astra 3.3.1 修复的移除守卫，低于该版本时不会触发。

### 5.3 角色句柄

```lua
local role = astra.role.find("chaos_elite_rifleman")
print(role.id, role.name, role.maxHealth, role.team)
print(role.waveSpawnCount)
```

常用成员：`id`、`name`、`description`、`roleType`、`team`、`spawnFlags`、`changeReason`、`scale`、`gravity`、`showCustomInfo`、`maxHealth`、`items`、`clearInventory`、`waves`、`waveSpawnCount`、`createInstance()`。

> 句柄上的可写属性 setter 是空实现（读取正常，赋值不会回写定义）；只读属性赋值会直接报错。**修改定义后必须重新 `astra.role.define`**，Astra 会替换定义对象。

---

## 6. `astra.wave`

### 6.1 方法

| 方法 | 返回 | 说明 |
|---|---|---|
| `astra.wave.define(def)` | `string` | 定义或更新波次，返回 `id` |
| `astra.wave.spawn(id, players?)` | `table` | 对指定玩家生成波次，返回本次生成的玩家数组 |
| `astra.wave.find(id)` | `wave` \| `nil` | 按 Id 查波次 |
| `astra.wave.list()` | `table` | 全部已注册波次 |
| `astra.wave.scheduler()` | `table` | 新建一个独立的加权调度器句柄 |

`spawn` 的 `players` 可以是玩家句柄、`userId` 字符串、`networkId` 数字，或它们的数组；省略/`nil` 表示 `Player.List`（全部玩家）。找不到波次 Id 会抛错。

### 6.2 定义字段

| 字段 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `id` | `string` | 必填 | 全局唯一，≤ 64 字符 |
| `name` | `string` | 必填 | 显示名 |
| `teams` | `table` | `{}` | 参与阵营：自定义 Id 优先，否则按原版 `Team` 名解析；两者都不是时忽略该元素 |
| `weight` | `number` | `0` | 加权调度权重 |
| `min_players` | `number` | `0` | 最少在线人数 |
| `max_players` | `number` | `-1` | 最多在线人数，`-1` 不限制 |
| `modify_threat` | `boolean` | `true` | 是否修改波次威胁值 |
| `threat` | `number` | `-1` | 自定义威胁值 |
| `spawn_protect` | `boolean` | `true` | 是否启用出生保护 |
| `spawn_protect_duration` | `number` | `-1` | 出生保护秒数，`-1` 用默认 |
| `announcement` | `string` | `nil` | 波次公告 |
| `announcement_translation` | `string` | `nil` | 公告翻译键 |
| `table` | `table` | `nil` | 角色生成表：`{ [自定义角色Id或RoleTypeId] = 数量 }` |
| `allow_spawn` | `function` | `nil` | `function(ply) -> boolean`，返回 `true` 才生成该玩家 |

`table` 的键先按已注册自定义角色匹配，再按原版 `RoleTypeId` 解析；还没有注册的自定义角色 Id 会被登记为 `RoleEntry.Custom`，等 Astra 刷新时按 Id 解析。

### 6.3 调度器句柄

每次 `astra.wave.scheduler()` 创建一个**独立**句柄，状态互不共享（同一 mod 内可创建多个）。

| 方法 | 返回 | 说明 |
|---|---|---|
| `scheduler.load(ids)` | `number` | 装载波次 Id 数组，返回装载数量 |
| `scheduler.advance(playerCount?)` | `wave` \| `nil` | 按权重抽取下一波；省略人数时用 `Player.Count` |
| `scheduler.current()` | `wave` \| `nil` | 当前波次 |
| `scheduler.remaining()` | `number` | 剩余待抽波次数量 |
| `scheduler.reset()` | `nil` | 清空调度状态 |
| `scheduler.support_register(id, weight?)` | `true` | 注册支援波次，默认权重 `0` |
| `scheduler.support_pick(playerCount?)` | `wave` \| `nil` | 抽取支援波次，并更新 `current()` |

```lua
local sched = astra.wave.scheduler()
sched.load { "chaos_elite_wave", "mtf_wave" }

local nextWave = sched.advance()
if nextWave then
    astra.wave.spawn(nextWave.id)
end
```

`support_pick` 必须通过桥接内部路径调用，才能同步当前波次；直接改注册表不会更新 `current()`。

### 6.4 波次句柄

常用成员：`id`、`name`、`teams`、`weight`、`minPlayers`、`maxPlayers`、`modifyThreat`、`customThreat`、`spawnProtect`、`spawnProtectDuration`、`announcement`、`announcementTranslation`、`table`。

---

## 7. `astra.hint`

| 方法 | 返回 | 说明 |
|---|---|---|
| `astra.hint.show(ply, text, position?, duration?, font_size?, id?)` | `string` | 显示提示，返回 `messageId` |
| `astra.hint.clear(ply, id)` | `nil` | 移除指定 `messageId` 的提示 |

- `position`：`HintPosition` 枚举名，默认 `CenterTop`。
- `duration`：秒，默认 `3`。
- `font_size`：默认 `20`。
- `id`：省略时桥接生成 `astra:<32位guid>` 并返回；**`clear` 必须使用 `show` 返回的同一个 id**。
- HintServiceMeow 对空/不存在的 id 会抛 NRE；桥接已把异常转成 Lua 错误，但仍应保存并使用返回的 id。

```lua
local id = astra.hint.show(ply, "10 秒后撤离", "CenterTop", 10)
astra.hint.clear(ply, id)
```

---

## 8. `astra.audio`

| 方法 | 返回 | 说明 |
|---|---|---|
| `astra.audio.play(ply?, file, volume?)` | `number` | 对单个玩家或全服播放音频，返回播放 id；失败返回 `0` |
| `astra.audio.stop(ply?)` | `nil` | 停止单个玩家或全服播放 |

- 省略 `ply`（第一个参数直接给文件路径）表示全服；`file` 是 Astra 能解析的服务端音频文件路径。
- `volume` 默认 `1`。

```lua
astra.audio.play("chaos_elite.ogg", 0.8)          -- 全服
astra.audio.play(ply, "chaos_elite.ogg", 0.8)     -- 单人
astra.audio.stop(ply)
```

---

## 9. `astra.countdown`

| 方法 | 返回 | 说明 |
|---|---|---|
| `astra.countdown.start(delay, callback, tick?, repeat?)` | `number` | 启动倒计时，返回 id |
| `astra.countdown.cancel(id)` | `nil` | 取消倒计时 |

- `delay`：秒。
- `callback`：倒计时结束时调用，无参数。
- `tick`：可选，由 Astra 周期调用（约每秒一次）。
- `repeat`：可选谓词；结束时调用，返回 `true` 则重新开始，否则结束。
- 回调都在主线程执行；回调里的 Lua 异常只记日志，不影响倒计时框架。

```lua
local id = astra.countdown.start(10, function()
    cassie.message("Delta-9 撤离窗口关闭")
end)
astra.countdown.cancel(id)
```

---

## 10. `astra.effect`

| 方法 | 返回 | 说明 |
|---|---|---|
| `astra.effect.apply(ply, effects)` | `nil` | 施加一组效果 |
| `astra.effect.remove(ply, effects)` | `nil` | 移除一组效果 |

`effects` 是数组，每项：

| 字段 | 类型 | 默认值 | 说明 |
|---|---|---|---|
| `effect` | `string` | 必填 | `EffectType` 枚举名；为空整条报错 |
| `intensity` | `number` | `1` | `0`–`255`，越界自动截断 |
| `duration` | `number` | `0` | 持续秒数 |
| `add_duration` | `boolean` | `false` | 是否在现有持续时间上累加 |

```lua
astra.effect.apply(ply, {
    { effect = "Bleeding", intensity = 30, duration = 10 },
    { effect = "AmnesiaVision", intensity = 1, duration = 5, add_duration = true },
})

astra.effect.remove(ply, { { effect = "Bleeding" } })
```

---

## 11. 事件 `astraplayer`

桥接注册了一个事件类，暴露 Astra 的 `PlayerEvents`：

```lua
eventhook.bind("astraplayer", "Shooting", "elite_shot", function(ev)
    print(ev.Player.nickname)
end)
```

- 事件名与 `Astra.Events.PlayerEvents` 的 public 事件同名（如 `Shooting`、`Shot`、`Hurting` 等），完整列表可用 `eventhook.events("astraplayer")` 查看。
- 回调参数按事件参数顺序传入，每个参数是一张事件参数表（字段按该对象 public 成员映射，如 `ev.Player`）。
- 绑定时使用 `eventhook` 的通用规则：同一 `id` 解绑。

---

## 12. 生命周期、热重载与所有权

### 12.1 Id 所有权

- 内容 Id 在整个桥接内**全局唯一**，按大小写不敏感比较。
- 同一个 Id 只有最初定义它的 mod 能更新；其它 mod 再定义会抛：
  `astra: Id 'x' 已被模组 y 占用（不允许跨模组覆盖）`。
- 同一 mod 内重复 `define` = 更新定义对象：适配器实例复用，不重复注册，玩家身上已套用的角色会读取新定义。

### 12.2 热重载

`nekolua reload` 会重建 mod 的 Script，并触发 `LuaCleanupRegistry`：

1. 该 mod 的所有内容先标记 `Orphaned`；
2. 下一帧确认没有被同名重定义后，按 **角色 → 波次 → 阵营** 顺序注销（阵营被波次引用时 Astra 会拒绝注销）；
3. 注销角色前，先遍历在线玩家移除该角色实例；
4. 最后清空 mod 内容与对象缓存。

因此 `define` 可以写在 `main.lua` 顶层：重载后同名 Id 会“接管”旧条目，不会因重复注册失败。

### 12.3 插件卸载

NekoLua 禁用时 `AstraBridge.Unload()`：

- 注销插件就绪回调；
- 按依赖逆序注销全部已注册内容；
- 清空桥接注册表与对象缓存。

扩展 DLL 本身无法卸载；重新启用插件后以新的 `OnLoad` 状态重新装载。

---

## 13. 错误与排错

| 现象 | 原因 / 处理 |
|---|---|
| `astra.available == false` | Astra 版本低于 3.3.1，或 `Astra.Framework.dll` 未加载；看控制台 `[NekoLua.Astra]` 日志 |
| `需要 AstraFramework >= 3.3.1，当前: x.y.z` | 升级 Astra；桥接按 3.3.1 签名编译 |
| `Id 'x' 已被模组 y 占用` | 跨 mod Id 冲突；换 Id，或让原 mod 停止定义 |
| `必须在主线程调用（当前为后台线程）` | 写操作/hint/audio/countdown/effect 只能在主线程执行 |
| `该全服级 API 已被服务器禁用（Config.DeniedGlobalApis 含 '标签'）` | 运营者在 `DeniedGlobalApis` 里关闭了对应标签 |
| 提示无法清除 | `clear` 用了错误 id；保存 `show` 的返回值 |
| `on_removed` 不触发 | Astra < 3.3.1 的 `RemoveRole` 守卫写反；升级到 3.3.1 |
| 注册失败但无异常 | 看 `[NekoLua.Astra] ... 暂未注册成功` 警告；`astra.flush()` 会重试，最多 5 帧 |

桥接把 Astra/第三方抛出的 .NET 异常统一转成 Lua `ScriptRuntimeException`；Astra 回调内部抛出的异常只记录日志并返回 `nil` / `false`，不会中断波次或角色流程。

---

## 14. 版本历史

| 桥接基线 | 关键变化 |
|---|---|
| Astra 2.2.1 | 初版：实例注册/注销 API、波次角色生成器链、事件类注册 |
| Astra 3.0.1 | Entry 模型适配：`ItemEntry` / `TeamRef` / `RoleEntry`，支持自定义物品与混编阵营 |
| Astra 3.2.0 | 波次加权：`weight` / `min_players` / `max_players`、`astra.wave.scheduler()` |
| Astra 3.3.1 | 修复 `CustomRole.RemoveRole` 守卫，`on_removed` 与角色清理生效；当前最低版本 |

---

## 15. 已知限制

- 只覆盖 Astra 的团队/角色/波次 + hint/audio/countdown/effect；**没有** ability、item、patch 接口（上游未开放或明确不做）。
- 定义字段是白名单：不接受程序集名、类型名或任意反射入口。
- 角色/阵营/波次句柄的可写属性 setter 是空实现，不会回写定义；更新一律走 `define`。
- `wave.table` 中尚未注册的自定义角色 Id 会先登记，等 Astra 刷新解析；解析失败时该条目不会生成。
- 桥接编译期绑定 Astra 3.3.1 签名；升级 Astra 需要同步 `libs/astra/<版本>`、`MinimumAstraVersion` 与适配器代码。
