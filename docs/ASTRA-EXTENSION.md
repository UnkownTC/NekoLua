# NekoLua × AstraFramework 扩展方案（v1 已实现）

> 状态：**v1.3 已实现**（核心 `0a00d81`，桥接 `31c72ec`；已适配 Astra 7.0.0）。2026-11-21。
> 基线：NekoLua `2.2.0`（本仓库）/ AstraFramework `7.0.0`。
> 一句话：Lua 通过 `astra.*` 声明 `CustomTeam` / `CustomRole` / `CustomWave` 并操作它们；桥接是独立 `NekoLua.Astra.dll`，放 `NekoLua/extensions/`，核心不引用 Astra。
> Lua 侧参考手册见 [API-ASTRA.md](API-ASTRA.md)；NekoLua 核心 API 见 [API.md](API.md)。

## 实现状态（2026-11-21）

| 项 | 状态 |
|---|---|
| Astra 侧实例注册/注销（2.2.0）+ 按波次数量 / Table 可覆写 / 自动注册豁免（2.2.1） | 已完成（Astra 仓库） |
| NekoLua 扩展装载点（`extensions/`、`IRegisterLua` 注入、`ILuaExtension`、`LuaCleanupRegistry`、`RegisterEventClass`） | 已完成 |
| 桥接 v1：`astra.team/role/wave` + `astra.hint/audio/countdown/effect` | 已完成 |
| 热重载（同 Id 换定义、延迟一帧注销）、按 mod 回收、主线程校验、守卫标签 | 已完成 |
| Astra 3.0.1 适配：`Items` 改 `ItemEntry`（支持自定义物品 Id）、`Teams` 改 `TeamRef`（原版/自定义混编）、`Table` 改 `RoleEntry`、生成器实现 `ICustomWaveRoleSpawnerV2` | 已完成 |
| Astra 3.2.0 适配：波次可声明 `weight` / `min_players` / `max_players`（`IWeightedWave`），可直接进 `WaveScheduler` / `SupportWavePool` | 已完成 |
| Astra 3.3.1 修复：`CustomRole.RemoveRole` 守卫写反（`if (Check(player)) return;`），所有移除路径都不触发 `OnRemoved` 与属性回收；桥接最低版本提升到 3.3.1 | 已完成（Astra `41ebe5b`） |
| Astra 4.0.0 适配：编译依赖升级 `libs/astra/4.0.0/`；桥接为角色/波次/物品实现 `ISpawnOnRoundStart`（`spawn_points` / `max_spawn_count`），新增 `astra.item`（define/give/find/list）与 `astra.position`（teleport/resolve），team/role/wave 原有字段不变 | 已完成 |
| Astra 5.0/6.0 适配：上游 `ISpawnOnRoundStart` → `ISpawnable` → `ISpawnDefinition`，`SpawnPoints` 改名 `CustomSpawnPoints` 并移除显式 opt-in；桥接适配器跟进实现 `ISpawnDefinition` | 已完成 |
| Astra 6.1.0 适配：上游新增 `ISpawnCondition`；桥接实现 `ISpawnDefinition, ISpawnCondition`，角色/波次/物品新增 `min_amount` / `can_spawn` | 已完成 |
| Astra 7.0.0 适配：上游把 `MinAmount` / `MaxSpawnCount` 拆到 `ISpawnOnRoundStart : ISpawnDefinition`；桥接适配器改为实现 `ISpawnOnRoundStart` + `ISpawnCondition`，最低版本升至 7.0.0 | 已完成 |
| `astra.ability`（v2）、`astra.effect.define`（v2）、`astra.patch`（不做）、物品事件（`OnShooting` 等） | 未实现 |
| 实机冒烟（游戏内加载示例 mod） | 待服务端验证 |

与设计稿的差异：

1. Astra 实际从 **2.2.1** 起步（设计稿写 2.2.0）：额外提供 `IWaveSpawnCountProvider`（按波次数量）与 `[DisableAutoRegister]` 内容豁免；当前基线已到 **7.0.0**（见状态表）。
2. Astra 不可用时**不是没有 `astra` 表**，而是 `astra.available = false`、调用抛明确错误（扩展装载器仍然实例化了表提供者）。
3. `astra.role.define` 的定义回调 v1 只支持 `on_added` / `on_removed`；其它事件用 `eventhook.bind("astraplayer", "Shooting", ...)` 或 LabAPI 事件。
4. 波次刷新数量：`wave_count` 为统一数量；需要按波次不同数量时用 `wave_counts = { ["waveId"] = n }`。
5. **Astra 3.0 是 BREAKING**：桥接已跟进 Entry 化；Lua 侧语义不变，只是 `items` 现在也能写自定义物品 Id、`teams` / 波次 `table` 也能写原版阵营 / 原版角色名。低于 7.0.0 的 Astra 会直接 `astra.available=false`。
6. **Astra 3.1/3.2 是增量能力**：`astra.wave.define` 增加了 `weight` / `min_players` / `max_players`，Lua 定义的波次实现了 `IWeightedWave`，可交给 `WaveScheduler.Load/Advance` 或 `SupportWavePool.Register/Pick` 使用；桥接本身不替脚本调度波次。

---

## 0. TL;DR

| 决策 | 结论 | 理由 |
|---|---|---|
| 桥接放哪 | 独立程序集 `NekoLua.Astra.dll`，放 `NekoLua/extensions/`，由 NekoLua 扫描加载 | Astra 的 `AutoRegister` 会扫**所有插件程序集**的类型；桥接若做成插件，适配器类型会被误当成模板自动注册 |
| Lua 定义的内容怎么进 Astra | 每种内容一个 C# 适配器类（`LuaCustomRole/Team/Wave`），一个 Id 一个实例，定义存在桥接的注册表里 | Astra 目前只接受 **Type** 注册并用 `Activator` 实例化，Lua 无法生成类型；必须由 Astra 补实例注册 API |
| 什么时候注册 | 先入队，等 `OnServerPluginsEnabled`（Astra 自动注册完成后）统一提交 | `CustomTeam/CustomWave.Initialize()` 在注册表非空时**直接早退**，提前注册会把 Astra 自己的 C# 模板全部饿死 |
| 热重载 | 同 Id 重定义 = 原子换定义对象，不重复注册；mod 删除 = 延迟一帧注销 | 避免 Astra 报 Id 冲突，也避免 reload 时玩家角色被强制摘除 |
| 暴露面 | v1 只做 team / role / wave + hint / audio / countdown / 原版 effect；ability v2、item v3、patch 永不做 | 前四类是「声明式」的，适配器可控；item/ability 是重度子类化，收益/成本比低；Harmony 无法从 Lua 生成 |

预计工作量：Astra ≈ 250 行，NekoLua 核心 ≈ 200 行，桥接 ≈ 1300~1600 行（含文档与示例）。

---

## 1. 目标与非目标

**目标**

1. Lua mod 可以声明并注册 `CustomTeam` / `CustomRole` / `CustomWave`，与 C# 写的模板在同一张注册表里共存。
2. 运行时 API：`give / remove / get / list / find / spawn`，行为与 Astra 原生角色一致（阵营敌对、出生保护、波次表）。
3. 复用 Astra 的其它能力：`HintApi` / `AudioApi` / `CountdownApi` / 原版 `EffectEntry`。
4. 热重载安全：`nekolua reload` 不产生重复注册、不泄漏回调；mod 目录删除后 Astra 注册表能回收。
5. NekoLua 保持**不引用 Astra 也能独立编译发布**。

**非目标**

- 不从 Lua 写 Harmony 补丁（Lua 生成不了原生方法；`PatchMethod` 一律 C# 侧声明）。
- 不向 Lua 暴露 C# 类型名、`System.Type`、反射入口（沙箱逃逸面）。
- v1 不做物品事件（`OnShooting` / `OnUsing` 等）与自定义技能（`CustomAbility` 家族）——它们要求子类化并接管行为，放后续版本；物品定义本身（承载原版 `base_item` + 刷怪点）已在 v1 提供 `astra.item`。
- 不改变 Astra 现有 C# 工作流；存量模板的注册顺序、生命周期保持原样。

---

## 2. 事实核查（决定方案形态的硬约束）

### 2.1 Astra 侧

| # | 事实 | 位置 | 影响 |
|---|---|---|---|
| A1 | 模板只按 **Type** 注册，内部 `Activator.CreateInstance` 实例化 | `Roles/CustomRoleTemplateManager.cs:23`、`Teams/CustomTeam.cs:129-163`、`Waves/CustomWaveManager.cs:46-90` | Lua 无法产出 Type，必须补「按实例注册」 |
| A2 | `CustomRoleTemplateManager` 是 `internal`，角色**没有任何公开注册入口**；team/wave 只有公开的 `RegisterTemplate(Type)` | `Roles/CustomRoleTemplateManager.cs:10`、`Teams/CustomTeamApi.cs:40`、`Waves/CustomWaveApi.cs:42` | 至少角色注册必须改 Astra |
| A3 | 没有注销入口，只有框架 `Disable` 时整体 `Clear()` | `Loader/AutoRegister.cs:200-217` | 桥接无法在 mod 删除时回收内容，需补 `Unregister` |
| A4 | `Initialize()` 在注册表非空时**早退** | `Teams/CustomTeam.cs:131-132`、`Waves/CustomWaveManager.cs:48-49` | 注册时机必须晚于 Astra 自动注册，否则 C# 模板全部丢失 |
| A5 | `CustomWave.Table` 是只读非虚属性，由「实现了 `IWaveSpawnRole` 的角色」聚合；`WaveSpawnCount` 是**单个 int** | `Waves/CustomWave.cs:42-56`、`Waves/IWaveSpawnRole.cs:12-18` | 波次刷新数量只能写在角色上，且一个角色在所有波次里同数量 |
| A6 | 波次角色生成器是**全局单槽** | `Waves/ICustomWaveRoleSpawner.cs:25-41`、`Waves/CustomWaveApi.cs:49-54` | 桥接要么独占，要么和别的宿主抢；建议上游改成链式 |
| A7 | 模板事件用「实例」登记，同类型多实例没问题 | `Events/EventManager.cs:44-78`、`Roles/CustomRoleTemplateManager.cs:37` | 适配器可以一号一实例，事件回收按实例走 |

### 2.2 NekoLua 侧

| # | 事实 | 位置 | 影响 |
|---|---|---|---|
| N1 | 全局表是插件化的（遍历 `LuaRegister.Registers`），加一张表 Loader 零改动 | `Loader/LuaLoader.cs:190-196` | 桥接只需实现 `IRegisterLua` |
| N2 | 但注册只扫**自身程序集** | `MainPlugin.cs:26,32`、`Registers/LuaRegister.cs:9-28` | 需要扩展目录扫描 |
| N3 | 回收清单逐行硬编码（`CleanupMod` 8 项、`Disable` 10+ 项） | `Loader/LuaLoader.cs:124-134`、`MainPlugin.cs:44-63` | 桥接的内容回收没地方挂，需要一个小注册表 |
| N4 | `eventhook` 只认 `LabApi.Events.Handlers.*Events` | `Dependencies/LuaEventHook.cs:220-242` | Astra 的 `PlayerEvents.Shooting/Shot` 需要注册事件类的公开入口 |
| N5 | 路径模型没有 extensions 目录 | `Loader/LuaPaths.cs:20-34` | 需要新增 `Root/extensions` |
| N6 | 已有主线程队列与对象代理（`LuaObjectCache`/`LuaTypeMapper` 都是 public） | `Dependencies/LuaMainThread.cs`、`LuaObjectCache.cs:7` | 桥接可以直接复用，不用重造 |

### 2.3 由此得到的三条硬约束

1. **Astra 必须补公开的实例注册/注销 API**——否则 Lua 定义的角色/阵营/波次根本进不去。
2. **桥接不能做成 LabAPI 插件**——`AutoRegister.RegisterAll` 扫的是 `PluginLoader.Plugins.Values`（`Loader/AutoRegister.cs:42-59`），适配器类会被当成模板自动实例化（`Id` 从空定义里取，直接崩）。
3. **注册必须发生在 Astra 自动注册之后**——否则触发 A4 的早退，线上所有 C# 模板消失。

---

## 3. 总体架构

```
plugins/
  NekoLua.dll                     NekoLua 核心（不引用 Astra）
  AzureFramework.dll              Astra 本体
NekoLua/
  extensions/
    NekoLua.Astra.dll             桥接（引用 NekoLua.dll + Astra.Framework.dll + LabAPI）
  mods/<port>/<mod>/main.lua      Lua 玩法（require "astra" 的用法就是全局表）
```

三层职责：

| 层 | 负责 | 关键类 |
|---|---|---|
| L0 核心 | 扫扩展目录、装载 `IRegisterLua`、暴露回收/事件类注册钩子 | `LuaExtensionLoader`、`LuaCleanupRegistry`、`LuaEventHook.RegisterEventClass` |
| L1 桥接 | 定义解析、适配器、注册窗口、生命周期、守卫 | `AstraBridge`、`LuaCustomRole/Team/Wave`、`LuaContentRegistry` |
| L2 Lua | `astra.*` 表（每个 mod 独立实例） | `AstraRegister : IRegisterLua` |

加载时序（关键）：

```
Astra.Enable()                     → 注册 MainManager（Priority=Highest，先于 NekoLua）
NekoLua.Enable()
  ├ LuaExtensionLoader.LoadAll()   → Assembly.Load(byte[]) + new IRegisterLua() + OnLoad()
  └ LuaLoader.LoadAll()            → 每个 mod 执行 main.lua；astra.*.define 只入队（Pending）
所有插件 Enable 完毕
LabAPI OnServerPluginsEnabled
  ├ Astra.MainManager              → AutoRegister：Team→Wave→Item→Role 全部就位
  └ Bridge.Handler                 → Flush：Team→Wave→Role 依次实例注册
第一帧 Update                       → 之后 define 直接走"已初始化"路径
```

---

## 4. AstraFramework 上游改动（已落地 2.2.x / 3.0.x / 3.2.x / 3.3.x）

> 只加 API，不改现有签名；存量 C# 模板行为不变。

### 4.1 新增公开 API

```csharp
namespace Astra.Roles;
public static class CustomRoleApi
{
    /// <summary>按实例注册角色模板（Lua/外部框架用）。返回 false 表示 Id 冲突或校验失败。</summary>
    public static bool Register(CustomRole template);
    /// <summary>注销角色模板。调用方负责先摘除仍持有该角色的玩家。</summary>
    public static bool Unregister(string id);
}

namespace Astra.Teams;
public static class CustomTeamApi
{
    public static bool Register(CustomTeam team);
    public static bool Unregister(string id);
}

namespace Astra.Waves;
public static class CustomWaveApi
{
    public static bool Register(CustomWave wave);
    public static bool Unregister(string id);
    /// <summary>链式角色生成器：按注册顺序尝试，首个成功即结束。</summary>
    public static void AddRoleSpawner(ICustomWaveRoleSpawner spawner);
}
```

### 4.2 语义（实现要点）

- **Register 必须支持「Initialize 之后」**：直接写入注册表 + `EventManager.RegisterInstance` + 反向关联，**不得**碰 `Templates` 列表，也不得触发 `Initialize()`（否则命中 A4）。
- **Team**：`Registry[id] = team; NotifyRegister(team);` 已存在的波次如果引用了该 Id……需要先注册 team 再注册 wave，顺序由桥接保证。
- **Wave**：`Registry[id] = wave; foreach (var t in wave.Teams) t.AddWave(wave); NotifyRegister(wave);`
- **Role**：`RegisteredRoles[id] = role; EventManager.RegisterInstance(role, role.GetType()); if (role is IWaveSpawnRole s) foreach (var w in s.Waves) w.AddRole(role);`
- **Unregister**：注册表移除 → `CustomHandlersManager.UnregisterEventsHandler(instance)` → `OnUnregisterEvents()`；`EventManager` 补一个 `UnregisterInstance(handler)`（现在的 `UnregisterAll` 只能全清）。
- **冲突**：同 Id 不同来源直接拒绝并返回 false（不覆盖），日志给出「原占用者类型 → 新占用者」。
- **重复注册同 Id 同实例**：幂等返回 true（桥接热重载会走这条）。

### 4.3 spawner 链（A6）

`SetRoleSpawner` 保留（兼容），新增 `AddRoleSpawner` 内部用 `List<ICustomWaveRoleSpawner>` 顺序尝试。桥接注册的 spawner 只处理自己注册表里存在的角色 Id，返回 false 让链继续。

### 4.4 同期落地的小改（2.2.1）

- `CustomWave.Table` 改成 `virtual`，或在 `IWaveSpawnRole` 增加 `int GetWaveSpawnCount(CustomWave wave)`——解决 A5「多波次同数量」的硬伤。不做的代价：Lua 侧只能声明「角色属于哪些波次 + 统一数量」。
- `AutoRegister.RegisterType` 对 `[DisableAutoRegister]` 的 `CustomRole/Team/Wave` 也跳过——给外部桥接多一层保险（桥接本身不在插件程序集里，但用户可能手抖把 DLL 扔进 `plugins/`）。

---

## 5. NekoLua 核心改动（最小集，约 200 行）

### 5.1 扩展目录

`Loader/LuaPaths.cs` 增加：

```csharp
public static DirectoryInfo Extensions { get; }   // Root/extensions
```

### 5.2 扩展装载器

新增 `Loader/LuaExtensionLoader.cs`：

```csharp
public interface ILuaExtension
{
    string Id { get; }
    void OnLoad();    // 主线程，LuaLoader.LoadAll 之前
    void OnUnload();  // Disable 时
}

public static class LuaExtensionLoader
{
    public static IReadOnlyList<IRegisterLua> Registers { get; }
    public static void LoadAll();   // 扫 extensions/*.dll，按文件名排序
    public static void UnloadAll(); // 调 OnUnload，清 IRegisterLua 实例
}
```

规则：
- 每个 DLL 独立 try/catch，一个坏扩展不影响其它扩展和主插件。
- 只实例化 public、非抽象、带无参构造的 `IRegisterLua`。
- **`Name` 重复直接拒绝并记 Error**（现在是静默 last-wins；`LuaRegister.Register` 同步加这条校验）。
- 加载失败只记日志，不阻断 `LoadAll()`。

`MainPlugin.Enable()` 的插入点：

```csharp
LuaRegister.Clear();
LuaRegister.Register(PluginAssembly);
LuaExtensionLoader.LoadAll();     // ← 新增
LuaGameObject.Create();
LuaLoader.LoadAll();
```

### 5.3 回收注册表

新增 `Dependencies/LuaCleanupRegistry.cs`：

```csharp
public static class LuaCleanupRegistry
{
    public static void Register(LuaModInstance owner, Action<LuaModInstance> cleanup);
    internal static void Cleanup(LuaModInstance mod);   // 由 LuaLoader.CleanupMod 调
    internal static void ClearAll();                    // 由 Disable 调
}
```

改动点只有两处：`LuaLoader.CleanupMod` 末尾加一行、`MainPlugin.Disable` 加一行。这是 FLOW.md §2「新增按 owner 持有资源的子系统必须同时登记两处」的正解，也顺带把以后所有扩展的回收问题一次解决。

### 5.4 事件类注册

`LuaEventHook` 增加：

```csharp
public static void RegisterEventClass(string luaName, Type eventsClass);
```

桥接在 `OnLoad` 里注册 `Astra.Events.PlayerEvents` → Lua 侧即可：

```lua
eventhook.bind("astraplayer", "Shooting", "no_friendly_fire", function(ev) ... end)
```

### 5.5 守卫标签

桥接的写操作走 `LuaGlobalApiGuard.EnsureAllowed(tag, api)`，新增标签：

| 标签 | 覆盖 |
|---|---|
| `astra` | 全部 astra.* 写操作（粗粒度开关） |
| `astra.role` | 定义/套用/移除角色 |
| `astra.team` | 定义阵营、改敌对关系 |
| `astra.wave` | 定义/执行波次 |

默认不拦（与现有「默认放行、按标签逐类关闭」的策略一致），`Config.DeniedGlobalApis` 的说明文本同步补上这四个标签。

---

## 6. 桥接设计（`NekoLua.Astra.dll`）

### 6.1 程序集与依赖

| 项 | 值 |
|---|---|
| 引用 | `NekoLua.dll`（扩展契约）、`Astra.Framework.dll`（`libs/astra/7.0.0/`）、LabAPI + 游戏程序集（与 NekoLua 同版本） |
| 目标框架 | `net481`，`PlatformTarget=x64` |
| 构建 | 复用 `tools/vsbuild.py` 形态，独立 csproj |
| 部署 | 只放 `NekoLua/extensions/`；**不要**放 `plugins/`（见 A1/A2） |
| 失败姿态 | Astra 缺失/版本低 → `OnLoad` 记 Error，Lua 侧 `astra` 表仍存在但 `available=false`，方法调用报最低版本；绝不半加载 |

### 6.2 定义模型（Lua 表 → 不可变 C# 定义）

```csharp
sealed class RoleDefinition
{
    public string Key;                    // "<modKey>:<id>"，注册表主键
    public string Id, Name, Description;
    public RoleTypeId RoleType;
    public string TeamId;                 // 自定义阵营 Id，空 = 由 RoleType 推导
    public float MaxHealth;
    public ItemType[] Items;
    public bool ClearInventory, ShowCustomInfo;
    public Vector3 Scale, Gravity;
    public RoleSpawnFlags SpawnFlags;
    public RoleChangeReason ChangeReason;
    public string[] Waves;                // 参与的波次 Id
    public int WaveSpawnCount;            // -1 = 填充剩余
    public ScriptFunction OnAdded, OnRemoved, OnChangedRole, OnHurt;
}
```

解析规则：
- 字段白名单，未知字段报错（帮作者抓错别字）。
- 枚举用 `Enum.TryParse(..., ignoreCase: true)`；不接受 `System.Type` 字符串。
- 一个字段出错 → 整条定义拒绝（不留半成品），错误信息带字段路径，如 `astra.role.define: max_health 必须是数字`。

### 6.3 适配器（核心 60 行）

```csharp
sealed class LuaCustomRole : CustomRole, IWaveSpawnRole
{
    private readonly string _key;
    public LuaCustomRole(string key) => _key = key;

    private RoleDefinition Def => LuaContentRegistry.RequireRole(_key);
    private LuaContentRegistry Registry => LuaContentRegistry.Instance;

    public override string Id => Def.Id;
    public override string Name => Def.Name;
    public override string Description { get => Def.Description; set { } }
    public override RoleTypeId RoleType { get => Def.RoleType; set { } }
    public override TeamRef Team => Def.TeamId is { Length: > 0 } t
        ? TeamRef.FromCustom(Registry.RequireTeam(t))
        : base.Team;
    public override float MaxHealth { get => Def.MaxHealth; set { } }
    public override ItemType[] Items { get => Def.Items; set { } }
    public override bool ClearInventory { get => Def.ClearInventory; set { } }
    public override Vector3 Scale => Def.Scale;
    public override Vector3 Gravity => Def.Gravity;

    public IReadOnlyList<CustomWave> Waves => Registry.ResolveWaves(Def.Waves);
    public int WaveSpawnCount => Def.WaveSpawnCount;

    public override CustomRole CreateInstance() => new LuaCustomRole(_key);

    protected override void OnAdded(Player player)  => Registry.Invoke(_key, Def.OnAdded, player);
    protected override void OnRemoved(Player player) => Registry.Invoke(_key, Def.OnRemoved, player);
}
```

要点：
- 玩家实例与模板实例都按 `_key` 查当前定义 → **换定义不用重建实例**。
- `CreateInstance()` 必须覆写：Astra 的默认实现是 `Activator.CreateInstance(GetType())`，会丢 `_key`。
- `Team` 每次取 `TeamRef`，阵营改关系即时生效。
- `OnChangedRole/OnHurt` 等按需覆写，语义与 Astra 基类一致。

`LuaCustomTeam` / `LuaCustomWave` 同构：`Id/Name` 读定义，`Teams` 解析成实例列表，`Announcement` 从定义来；`CheckAllowSpawn` 可挂 Lua 回调。

### 6.4 内容注册表与生命周期

```csharp
sealed class LuaContentRegistry
{
    Dictionary<string, ContentEntry> _entries;   // "mod:roleId" → 定义 + 状态
    enum State { Pending, Registered, Orphaned }
}
```

状态机：

```
define ──► Pending ──(Flush)──► Registered
   ▲                                │ CleanupMod
   │ 同 key 重定义                   ▼
   └────────────── Orphaned ──(一帧后仍孤儿)──► Unregister + Player 摘除
```

- **Pending**：Astra 尚未完成自动注册，只入队。
- **Registered**：已在 Astra 注册表；同 key 再 define 直接替换 `Def` 对象，零 Astra 调用。
- **Orphaned**：`LuaCleanupRegistry` 回调触发；**不立刻注销**，用 `LuaMainThread.Queue` 推到下一帧末检查——如果这期间同 key 被重新 define（`reload` 场景：`CleanupMod` → 立即 `RunEntry`），直接复活；否则真正注销。
- **注销顺序**：先遍历 `Player.List` 把持有该角色的玩家 `CustomRoleApi.RemoveRole` → 再 `Unregister(id)` → 再清定义与回调。

这条「延迟注销」让 `nekolua reload` 变成无感的定义热替换，玩家身上的角色不会被摘掉。

### 6.5 提交窗口

```csharp
// OnLoad：注册一个 CustomEventsHandler，只监听 OnServerPluginsEnabled
public override void OnServerPluginsEnabled()
{
    AstraBridge.Flush();   // 幂等：Team → Wave → Role 顺序注册全部 Pending
}
```

`AstraBridge.Flush()` 之后置 `_initialized = true`，后续 define 直接注册。若事件因故没到（其他加载器/测试环境），`astra.role.give`/`wave.spawn` 首次调用时兜底 Flush 一次。

### 6.6 回调与线程

- 所有 `astra.*` 写操作要求主线程：桥接在 `OnLoad` 记录线程 Id，非主线程直接抛 `ScriptRuntimeException`（与 AGENTS「主线程约束」一致）。
- 回调统一 `try/catch (InterpreterException → Logger.Error("[<mod>] <id> 回调异常: ..."))`，异常**不得**穿回游戏事件链。
- 玩家参数用 `LuaObjectCache.Wrap(player)`（与 `player.list()` 的句柄一致），事件参数用 `LuaTypeMapper.ScanInstance(ev)`（与 `eventhook` 一致）。

### 6.7 波次

```csharp
sealed class LuaRoleSpawner : ICustomWaveRoleSpawner
{
    public bool TrySpawn(string roleId, Player player)
        => LuaContentRegistry.Instance.TryGetRoleByPublicId(roleId) is { } e
           && CustomRoleApi.AddRole(player, roleId) is not null;
}
```

- 若 `CustomWaveApi.RoleSpawner is null` → `SetRoleSpawner(我们的)`。
- 若非空且不是我们 → 用 `AddRoleSpawner`（2.2 链式）；老版本则记 Error 并给出「请移除其它 spawner」的提示。
- `astra.wave.spawn(id, players)` 直接调 `CustomWaveApi.Spawn(wave, players)`；波次本身仍由 Lua 用 `timer`/`scheduler` 决定何时触发。

### 6.8 错误处理

| 场景 | 行为 |
|---|---|
| Astra 程序集不存在 | `astra` 表存在但 `available=false`；调用报「需要 AstraFramework >= 7.0.0」，并记录 `[NekoLua.Astra]` 日志 |
| Astra < 7.0.0 | 同上，`available=false`；桥接不注册事件类与角色生成器 |
| 定义字段非法 | 抛 `ScriptRuntimeException`，错误带字段名 |
| Id 重复（不同 mod） | 第二个 define 抛异常，提示占用者 |
| 同 mod 同 Id 重定义 | 替换定义，返回成功 |
| 回调异常 | 记日志，游戏侧继续 |
| 注销时玩家仍持有 | 先 `RemoveRole`，再注销 |
| 桥接未初始化就 spawn | 兜底 Flush 后执行 |

---

## 7. Lua API 设计

> 本节保留设计阶段的接口取舍与示例；**逐项签名、默认值与错误语义以 [API-ASTRA.md](API-ASTRA.md) 为准**。

### 7.1 元信息

```lua
astra.available          -- boolean，桥接可用
astra.version            -- "7.0.0"（Astra 版本）
astra.api_version        -- 1
astra.flush()            -- 手动提交（调试用）
```

### 7.2 阵营

```lua
astra.team.define{
  id = "chaos_elite", name = "混沌精英", color = "#FF8800",
  relation = "Custom",              -- Custom / AllFriendly / AllHostile（忽略大小写）
  vanilla_team = "ChaosInsurgency", -- 原版锚点
  keep_vanilla = false,             -- 未命中名单时是否沿用原版关系
  hostile = { "NineTailedFox" },    -- 原版 Team / Faction 名或自定义阵营 Id
  friendly = { "ChaosInsurgency" },
}
astra.team.find("chaos_elite")   -- 句柄（只读字段）
astra.team.list()                -- 数组，元素是阵营句柄
```

### 7.3 角色

```lua
astra.role.define{
  id = "chaos_elite_shotgunner", name = "精英霰弹手", description = "高血量混沌单位",
  role_type = "ChaosRifleman",      -- RoleTypeId
  team = "chaos_elite",             -- 自定义阵营 Id（可空，空则按 role_type 推导）
  max_health = 200,
  items = { "Shotgun", "Medkit" },  -- 原版 ItemType，或自定义物品 Id（3.0 起可混编）
  clear_inventory = true,
  show_custom_info = true,
  scale = {1,1,1}, gravity = {0,-9.8,0},
  spawn_flags = "All", change_reason = "Respawn",
  waves = { "chaos_wave" }, wave_count = 2,   -- 2 / -1=填充剩余
  on_added   = function(ply) print("added", ply.nickname) end,
  on_removed = function(ply) end,
}

astra.role.give(player.get("Steve"), "chaos_elite_shotgunner")  -- 返回 bool
astra.role.get(ply)          -- 当前自定义角色句柄或 nil
astra.role.remove(ply)
astra.role.list()
astra.role.find("chaos_elite_shotgunner")
```

### 7.4 波次

```lua
astra.wave.define{
  id = "chaos_wave", name = "混沌波次",
  teams = { "chaos_elite" },          -- 自定义阵营 Id 或原版 Team 名，可混编
  -- table = { ["chaos_elite_shotgunner"] = 2, ["ChaosRifleman"] = 2 },  -- 覆盖角色侧刷新型
  weight = 5, min_players = 4, max_players = -1,   -- 3.2+：加权波次 / 人数门槛（-1 = 不限）
  announcement = "混沌精英入场",
  spawn_protect = true, spawn_protect_duration = 8,
  modify_threat = true, threat = 1.2,
  allow_spawn = function(ply) return ply.isAlive end,
}
astra.wave.spawn("chaos_wave", player.list())  -- 返回实际生成的玩家表
astra.wave.find("chaos_wave")

-- 3.2+ 加权调度器（每个 mod 独立句柄，不影响其它插件）
local sched = astra.wave.scheduler()
sched.load({ "chaos_wave", "mtf_wave" })          -- 按 weight 排序
local nextWave = sched.advance()                   -- 返回满足 min_players 的波次句柄或 nil
sched.support_register("chaos_wave", 5)            -- 支援池：波次 + 权重
local support = sched.support_pick(player.count()) -- 按人数门槛抽一个支援波次
sched.reset()
```

> 3.0 起 `table` 的键可以是自定义角色 Id，也可以是原版 `RoleTypeId` 名；不定 `table` 时由角色侧 `waves` + `wave_count` / `wave_counts` 聚合。

> 7.0+：角色、波次和物品都可以加 `spawn_points`（世界坐标或房间/区域筛选 + 本地坐标）、`min_amount`、`max_spawn_count` 与 `can_spawn`，由 Astra 的 `RoundStartSpawnManager` 在原版分配完成后生成；桥接适配器实现 `ISpawnOnRoundStart`（继承 `ISpawnDefinition`）与 `ISpawnCondition`。字段见 `docs/API-ASTRA.md` §6.4。单个位置表的运行时传送/解析用 `astra.position.teleport/resolve`，见 §12。

### 7.5 其它 API 分级

| 级别 | API | 说明 |
|---|---|---|
| v1 | `astra.hint.show(ply, text, position, duration, font_size?, id?)` → messageId / `astra.hint.clear(ply, id)` | 包 `HintApi`；未传 id 时桥接自动生成并返回，`clear` 必须用同一个 id（HintServiceMeow 对空/不存在 id 会抛 NRE） |
| v1 | `astra.audio.play(file, volume?)`（全服）或 `astra.audio.play(ply, file, volume?)` / `astra.audio.stop([ply])` | 包 `AudioApi`，全服/单人两态；全服时第一个参数直接给文件路径 |
| v1 | `astra.countdown.start(delay, cb, tick_cb, repeat_cb)` / `astra.countdown.cancel(id)` | 包 `CountdownApi`；`repeat` 回调返回 true 表示继续 |
| v1 | `astra.wave.scheduler()` → `load/advance/current/remaining/reset/support_register/support_pick` | 3.2+ 加权调度器与支援池；句柄按 mod 独立 |
| v1 | `astra.effect.apply(ply, { {effect="Bleeding", intensity=1, duration=5} })` / `remove` | 只做 `EffectEntry.Vanilla`，自定义效果需 C# |
| 未实现 | `astra.token.*` | 取消令牌语义与 Lua 生命周期不匹配，暂不暴露（v2 候选） |
| v2 | `astra.ability.define{...}` | 适配 `StandardAbility`：键位/冷却/条件/触发回调；需要按键槽位与客户端设置下发 |
| v2 | `astra.effect.define{...}` | 适配 `CustomEffect`（需要状态机回调） |
| v3 | `astra.item.define{...}` | 适配 `CustomItem` 家族（射击/换弹/拾取全钩子） |
| 永不 | `astra.patch.*` | Lua 无法生成原生补丁方法；补丁继续由 C# 声明 |

### 7.6 示例 mod

```lua
-- mods/7777/chaos_elite/main.lua
astra.team.define{
  id = "chaos_elite", name = "混沌精英", color = "#FF8800",
  relation = "custom", vanilla_team = "ChaosInsurgency",
  keep_vanilla = false, friendly = { "ChaosInsurgency" },
}

astra.role.define{
  id = "chaos_elite_shotgunner", name = "精英霰弹手",
  role_type = "ChaosRifleman", team = "chaos_elite",
  max_health = 200, items = { "Shotgun", "Medkit" },
  waves = { "chaos_wave" }, wave_count = 2,
  on_added = function(ply)
    astra.hint.show(ply, "你是混沌精英", "CenterTop", 5)
  end,
}

astra.wave.define{
  id = "chaos_wave", name = "混沌波次", teams = { "chaos_elite" },
  announcement = "混沌精英入场", spawn_protect = true,
}

-- 每 120 秒刷一次
timer.every(120, "chaos_wave_timer", function()
  astra.wave.spawn("chaos_wave", player.list())
end)
```

---

## 8. 安全模型

| 风险 | 处理 |
|---|---|
| 定义注入任意类型 | 只接受白名单字段与枚举名，无 `System.Type`/程序集名 |
| 回调执行任意 Lua | 与普通 mod 同权，属于「脚本可信」模型；只做异常隔离，不上提权限 |
| 全服状态被脚本改写 | `LuaGlobalApiGuard` 标签（`astra.role` 等），运营者可按类关闭 |
| 桥接 DLL 被替换 | 扩展目录本身即信任边界；文档建议只放官方 DLL，可后续加 SHA256 白名单 |
| 玩家句柄逃逸 | 沿用 `LuaObjectProxy`，写操作仍受 `LuaTypeMapper.GuardWrites` 约束 |
| 线程竞争 | 写操作强制主线程；回调只在游戏事件链上执行 |

---

## 9. 兼容性与失败矩阵

| 条件 | 表现 |
|---|---|
| Astra 未安装 | NekoLua 正常启动；`astra` 表存在但 `available=false`，调用报“需要 AstraFramework >= 7.0.0” |
| Astra < 7.0.0 | 同上；桥接不注册事件类与角色生成器，日志明确提示最低版本 |
| NekoLua 旧版（无扩展钩子） | 桥接无法被加载（文件不被扫描），无副作用 |
| 定义冲突（跨 mod） | 后者 define 报错，前者不受影响 |
| reload 期间 | 定义热替换，玩家角色保留；一帧后确认无重定义才注销 |
| mod 删除 / `refresh` | 内容注销 + 持有玩家角色摘除 |
| NekoLua `Disable` | 全部注销；Astra 侧模板清空（验证可用 `customrole list`） |
| 服务器重启 | 无残留（Astra 生命周期本就清空） |

---

## 10. 落地路线与验证

> 状态：P0–P4 全部已完成，桥接基线为 Astra 7.0.0。

| 阶段 | 内容 | 验证 |
|---|---|---|
| P0 | Astra 2.2：实例注册/注销 + spawner 链 + 事件注销 | Astra 自身 `dotnet test`/`vsbuild`；`customrole list` 前后一致；签名与 API 文档一致 |
| P1 | NekoLua 核心：extensions 目录 + 装载器 + 回收表 + 事件类注册 + 守卫标签 | 无额外 DLL 时构建/启动行为不变；坏 DLL 不影响主流程 |
| P2 | 桥接 v1：team/role/item/wave + hint/audio/countdown/effect | 示例 mod 实机：给角色、看血量/物品/阵营敌对、刷波次、回合开始刷物品 |
| P3 | 热重载与回收 | `reload` 两次后 `astra.role.list()` 数量不变；删 mod + `refresh` 后角色消失且玩家被摘除 |
| P4 | 文档与示例 | `docs/`、`mods/` 示例、失败矩阵逐条手测 |

核心冒烟脚本：

```lua
print(astra and astra.available, astra and astra.version)
print(#astra.role.list())
local p = player.list()[1]
print(astra.role.give(p, "chaos_elite_shotgunner"))
print(astra.role.get(p) ~= nil)
```

---

## 11. 风险与开放问题

**风险**

1. **Astra 上游 API 变动**：桥接编译期绑定 Astra 7.0.0 签名（Entry 模型）；升级需同步 `libs/astra/<版本>`、`MinimumAstraVersion` 与桥接代码，低于下限会直接 `astra.available=false`。
2. **Initialize 早退**（A4）：只要有人绕过 Flush 提前注册，C# 模板会整体消失；桥接自身守住了时序，但挡不住第三方扩展抢跑。当前未做模板数启动自检，出现时表现为原版 C# 模板消失，需按 Flush 顺序排查。
3. **注销与玩家实例**：`Unregister` 只清模板，玩家身上的实例要调用方自己摘；桥接已内置，但 C# 侧直接用新 API 的人容易忘。
4. **程序集不可卸载**：NekoLua 用 `Assembly.Load(byte[])` 装载扩展，DLL 文件不再被锁、运行期可直接覆盖；但已加载的旧代码要重启（或重新启用 NekoLua 插件）才会换新。`OnUnload` 必须把静态状态清干净，`OnLoad` 必须幂等；扩展也不能依赖 `extensions/` 里的其它 DLL。
5. **单槽 spawner**：Astra 3.x 仍是全局单槽角色生成器；若已有其它宿主占用，波次生成会静默失败，桥接会记 Error。
6. **性能**：定义解析在 define 时一次完成；运行期只做字典查找。`Role.Team` 每次构造 `TeamRef`，可缓存但不要缓存 `CustomTeam` 实例（跨回合复用没问题，但最好按 Id 查）。

**开放问题**

1. **后续 Astra 版本的兼容上限**：桥接只校验最低版本（7.0.0）；角色/波次/物品的回合开始生成已桥接，位置/传送 API 已通过 `astra.position` 暴露，仅剩物品事件（`CustomItem.OnShooting` 等）未桥接；再改 Entry / 注册模型需要重新适配，升级前关注 Astra 发布说明。
2. **per-wave 角色数量**：目前 `wave_count` 统一值 + `wave_counts` 覆盖；更细粒度取决于 Astra 上游 `IWaveSpawnCountProvider` 的演进（A5）。
3. **v2 ability / 物品事件**：v1 已明确不做；是否投入取决于玩法是否真的需要按键技能 / 自定义物品行为（各约 +500 行）；物品定义与刷怪点已可用。
