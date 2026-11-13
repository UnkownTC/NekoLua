# AGENTS.md — NekoLua 协作约定

> 适用于本仓库全部代码。优先级：本文件 > 代码注释 > 个人习惯。
> **宽松原则**：本仓库是单插件仓库，绝大多数约束只求「可追溯、可编译、不破坏 Lua 脚本兼容性」，不设强制评审门禁。

---

## 0. 项目定位

NekoLua 是 SCP: Secret Laboratory 服务端 Lua 脚本插件（LabAPI + MoonSharp），提供 Lua 运行时、游戏内容代理层与 RA 指令。

| 是 | 不是 |
|---|---|
| 独立的 LabAPI 插件 | Site19 业务仓库的一部分 |
| Lua 沙箱 + 代理桥接层 | 游戏内玩法内容 |
| 服务端能力 | 客户端模组 |

---

## 1. 文件地图

| 路径 | 职责 | 何时看 |
|---|---|---|
| `AGENTS.md` | 本文件：协作与提交约定 | 每次动手前 |
| `src/NekoLua/FLOW.md` | 加载流程与执行模型（权威流程图） | 改 Loader / 调度时 |
| `src/NekoLua/Loader/` | Lua 引擎启动、路径解析、Mod 实例、扩展装载（`LuaExtensionLoader`） | 改加载链路时 |
| `src/NekoLua.Astra/` | 可选桥接工程：AstraFramework → Lua（`astra.*`），核心不引用 | 改 `astra.*` 时 |
| `src/NekoLua/Proxies/` | 暴露给 Lua 的代理对象（`Lua*Proxy`） | 新增/修改 API 时 |
| `src/NekoLua/Registers/` | 代理注册入口（`IRegisterLua` 实现） | 注册新代理时 |
| `src/NekoLua/Dependencies/` | 基础设施：调度、事件总线、守卫、序列化 | 改底层工具时 |
| `src/NekoLua/Commands/` | RA 指令 | 加指令时 |
| `libs/` | 只读依赖（游戏与 LabAPI 程序集） | 需要新引用时 |
| `tools/vsbuild.py` | 构建入口（net481 必需） | 每次构建 |
| `docs/` | 扩展契约与桥接方案（`EXTENSIONS.md` / `ASTRA-EXTENSION.md`） | 加扩展 / 写文档时 |
| `artifacts/` | 构建产物与中间文件（不提交） | 排查失败时 |

---

## 2. 三条铁律

1. **可回滚**：任何改动前，当前状态应能通过 `git checkout` 恢复；不制造不可逆状态。
2. **不破坏 Lua 脚本兼容**：删改 `Proxies/` 中暴露给 Lua 的成员属于破坏性变更，需升 MINOR/MAJOR 并更新文档。
3. **可追溯**：每次改动必须提交，提交信息包含改动范围与动机。

除此之外不设额外流程——不强制 PR、不强制任务卡、不强制双人评审。

---

## 3. 分支与提交

### 3.1 分支

| 场景 | 建议 |
|---|---|
| 单人小改动（1~2 个文件） | 直接提交 `main` |
| 新代理 / 重构 | 简短分支 `feat/<要点>`、`refactor/<要点>`，完成后合并 |
| 实验性改动 | 随意分支，推不推都行 |

不做强制分支命名规范；**唯一要求**：不要在同一分支上重写他人已推送的提交历史。

### 3.2 提交信息

```
<type>: <一句话说明>

<可选：为什么这么改、影响范围、破坏性变更提示>
```

`type`：`feat` `fix` `refactor` `perf` `docs` `build` `test` `chore`。

**必须**：能看懂改了什么。
**不必**：任务 ID、模型名、时间戳。

### 3.3 一次提交一件事

允许相邻小修合并，但不要把「加代理 + 改加载 + 修 bug」塞进一个提交。

---

## 4. 构建与验证

```bash
dotnet restore src/NekoLua/NekoLua.csproj
python tools/vsbuild.py src/NekoLua/NekoLua.csproj -p:Configuration=Release -p:Platform=x64

# 可选桥接（需要 libs/astra/2.2.1/Astra.Framework.dll）
dotnet restore src/NekoLua.Astra/NekoLua.Astra.csproj
python tools/vsbuild.py src/NekoLua.Astra/NekoLua.Astra.csproj -p:Configuration=Release -p:Platform=x64
```

| 要求 | 强度 |
|---|---|
| 编译 0 error | **必须** |
| 不新增 warning | 建议 |
| `-warnaserror` | 发布前建议跑一次，日常不强制 |
| Lua 侧冒烟 | 改代理/注册后**必须**实机加载一个脚本验证（`lua` 指令或 `RefreshCommand`） |

**注意**：`net481` + 公开化程序集必须走 `tools/vsbuild.py`；`dotnet build` 会报 MSB4803。

产物：`artifacts/build/NekoLua/x64/Release/NekoLua.dll`

---

## 5. 代码约定（宽松版）

### 5.1 命名空间

- 一个目录一个命名空间，目录名 = 命名空间末段（`Proxies/IO/` → `NekoLua.Proxies.IO`）
- 暴露给 Lua 的类统一 `Lua*Proxy` 命名，集中在 `Proxies/`
- 新增能力 → 在 `Proxies/` 加类 + 在 `Registers/` 登记，不要在 Loader 里散着写

### 5.2 可见性

```
Lua 需要调用的成员 → public
仅插件内部使用     → internal
```

- `public` 成员必须有 XML 文档（`/// <summary>`），因为它们是 **Lua 侧的 API 契约**
- `internal` 类型不得出现在任何 `public` 签名中

### 5.3 Lua 边界规则

| 规则 | 说明 |
|---|---|
| 异常不跨界 | 代理方法内 `try/catch`，转成 Lua 可见的错误信息，不要抛 .NET 异常进 Lua 虚拟机 |
| 守卫必过 | 文件/网络/全局访问必须经过 `LuaPathGuard` / `LuaNetworkGuard` / `LuaGlobalApiGuard`，不得直接调用 `File`/`HttpClient` |
| 主线程约束 | 触碰 Unity / 游戏对象必须走 `LuaMainThread` 或调度器，禁止在 Lua 工作线程直接操作 |
| 缓存交托管 | 对象引用使用 `LuaObjectCache`，避免 Lua GC 与 Unity 生命周期冲突 |

### 5.4 风格

- 优先 `file-scoped namespace`，`using` 分组（System → NekoLua → LabApi → 其它）
- 一个文件一个公开类型
- 不为「可能将来用得上」而抽象；不写空的预留接口

---

## 6. API 变更规则

| 变更 | 版本 | 额外要求 |
|---|---|---|
| 新增代理/方法 | MINOR | 更新 `FLOW.md` 或 README 的 API 清单 |
| 修改/删除 Lua 可见成员 | **MAJOR** | 提交信息标注 `BREAKING`，说明脚本迁移方式 |
| 内部实现调整 | PATCH | — |

新增 Lua 可见 API 前先问：「脚本真的需要它吗？」——暴露面越大，越难维护，也越容易成为沙箱逃逸口。

---

## 7. 交付说明格式（简版）

改动完成时给出：

1. **改了什么**（文件/类型清单）
2. **怎么验证的**（构建结果、脚本冒烟结果）
3. **有什么风险**（破坏性变更、未验证假设）

不需要五段式报告，也不需要任务卡；说清楚这三点即可。

---

## 8. 边界

| 不做 | 说明 |
|---|---|
| 修改 `libs/` 里的 DLL | 只读区；需要新版就新增目录并登记 |
| 依赖 Site19 业务工程 | 本仓库必须能单独编译、单独发布 |
| 自建远程仓库 / 推送 | 由仓库所有者操作 |
| 在代理层暴露 `System.Reflection` / 任意 IO | 沙箱逃逸风险 |
