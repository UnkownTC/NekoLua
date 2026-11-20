# NekoLua

NekoLua 是 **SCP: Secret Laboratory** 服务端 Lua 脚本插件，基于
[LabAPI](https://github.com/northwood-studios/LabAPI) 与 [MoonSharp](https://www.moonsharp.org/)。
它把 Lua 运行时、游戏对象代理层与 Remote Admin 指令装进服务器，让脚本作者不写 C#、
不重编译插件，就能实现大多数服务端玩法。

- **目录隔离**：每个 mod 独立目录、独立脚本实例、独立全局表，互不污染。
- **热重载**：`nekolua reload` 重建脚本；timer / eventhook / command / eventbus / scheduler
  的注册按 mod 回收，`storage` 跨重载保留。
- **托管对象代理**：player / item / door / room / elevator 等游戏对象以句柄暴露，
  `obj.prop` 读写属性、`obj:method()` 调用方法，无需为每个对象手写包装。
- **沙箱与守卫**：脚本运行在 MoonSharp `Preset_HardSandbox` 下，没有 `io` / `os` /
  `debug` / `loadfile`；文件、网络、进程、Win32 等能力必须走受守卫的代理表。
- **可扩展**：第三方 DLL 放进 `NekoLua/extensions/` 即可注册新的 Lua 全局表或生命周期钩子。
- **Astra 桥接（可选）**：把 [AstraFramework](https://github.com/UnkownTC/AstraFramework)
  的 CustomTeam / CustomRole / CustomItem / CustomWave、回合开始刷怪点、位置传送、
  Hint / 音频 / 倒计时 / 特效与波次调度器暴露给 Lua。

> 当前文档与二进制基线：Lua 运行时 **MoonSharp 2.0.0**，可选桥接 **AstraFramework 6.1.0**。

## 环境要求

| 依赖 | 要求 |
|---|---|
| 服务端 | SCP: Secret Laboratory Dedicated Server，自带 LabAPI |
| 运行时 | .NET Framework 4.8.1（`net481`） |
| 可选桥接 | AstraFramework **>= 6.1.0** 已作为 LabAPI 插件安装 |
| 开发构建 | .NET SDK + Python 3（`tools/vsbuild.py`，`dotnet build` 会报 MSB4803） |

## 安装

### 1. 安装 NekoLua 本体

从 [Releases](https://github.com/UnkownTC/NekoLua/releases) 下载 `NekoLua.dll`，放到 LabAPI 的插件目录：

```text
%APPDATA%\SCP Secret Laboratory\LabAPI\plugins\global\NekoLua.dll     # 所有端口生效
%APPDATA%\SCP Secret Laboratory\LabAPI\plugins\<端口>\NekoLua.dll     # 仅指定端口
```

Linux 下对应 `~/.config/SCP Secret Laboratory/LabAPI/plugins/...`。启动服务器后，NekoLua 会在
`%APPDATA%\SCP Secret Laboratory\SecretLab\NekoLua\` 下创建运行目录：

```text
SecretLab/NekoLua/
  mods/<port>/<mod>/main.lua      端口专属 mod（入口固定为 main.lua）
  mods/global/<mod>/main.lua      全局 mod
  dependencies/<port>/ | global/  Lua 依赖
  configs/<port>/<mod>.json       mod.registercfg() 落盘位置
  extensions/*.dll                扩展/桥接程序集
```

### 2. 安装 Astra 扩展（可选）

需要先安装 **AstraFramework >= 6.1.0**（它本身是 LabAPI 插件），然后把
`NekoLua.Astra.dll` 放到：

```text
SecretLab/NekoLua/extensions/NekoLua.Astra.dll
```

注意：

- **不要**把 `NekoLua.Astra.dll` 放进 `LabAPI/plugins/`——它不是 LabAPI 插件，放错位置会被
  LabAPI 或 Astra 的自动扫描误加载。
- 扩展 DLL 用字节方式装载，运行期可以覆盖文件，但**新代码需要重启服务器**（或重新启用
  NekoLua 插件）才生效。
- 扩展不能依赖 `extensions/` 里的其它 DLL；依赖应放进 `LabAPI/plugins/` 或合并进同一个 DLL。
- 若 Astra 版本低于 6.1.0，`astra.available` 为 `false`，`astra` 表仍存在，但调用会报
  `需要 AstraFramework >= 6.1.0`。

### 3. 写第一个 mod

```lua
-- SecretLab/NekoLua/mods/global/hello/main.lua
log.info("hello, NekoLua")

eventhook.bind("server", "RoundRestarted", "hello_round", function()
    broadcast.send("新一轮开始，祝好运", 5)
end)

timer.delay(1.0, function()
    log.info(("在线玩家：%d"):format(player.count()))
end)
```

保存后在服务器控制台或 RA 执行 `nekolua refresh`，再执行 `nekolua list` 确认 mod 已加载。

### 4. 验证 Astra 桥接

```lua
astra.team.define({ id = "chaos_elite", name = "混沌精英", color = "#FF8800" })
astra.role.define({ id = "chaos_elite_role", name = "精英", team = "chaos_elite", max_health = 150 })
astra.flush()
print(astra.available, astra.version, #astra.role.list())
```

完整字段与签名见 [docs/API-ASTRA.md](docs/API-ASTRA.md)。

## 指令

`nekolua` 同时注册到 Remote Admin 与服务器控制台，需要 `ServerConsoleCommands` 权限：

| 指令 | 别名 | 说明 |
|---|---|---|
| `nekolua list` | `l` / `ls` | 列出已加载的 mod |
| `nekolua reload [mod...]` | `r` / `rl` | 重载指定 mod；不带参数时重载全部已启用 mod |
| `nekolua refresh` | `rf` / `scan` | 重新扫描 mods 目录并加载 |

## 配置

插件配置位于 `SecretLab/NekoLua/configs/`，常用项：

| 配置 | 默认 | 说明 |
|---|---|---|
| `EnableNetwork` | `true` | 是否允许 `http` / `network` 表 |
| `BlockPrivateNetworkTargets` | `true` | 禁止访问内网地址 |
| `EnableProcess` | `false` | 是否允许 `process` 表 |
| `EnableWin32` | `false` | 是否允许 `win32` 表 |
| `EnableFtp` | `false` | 是否允许 FTP |
| `EnvWriteEnabled` | `false` | 是否允许 `env.set` |
| `AllowScriptRunCommand` | `false` | 是否允许脚本执行服务器命令 |
| `DeniedGlobalApis` | `[]` | 按标签禁用全服级 API |
| `MainThreadQueueLimit` | `4096` | 主线程队列上限 |
| `MainThreadTasksPerTick` | `64` | 每帧处理的主线程任务数 |

完整字段、默认值与说明见 [docs/API.md](docs/API.md) 第 13 章。

## 构建

```bash
# 核心（可单独发布）
dotnet restore src/NekoLua/NekoLua.csproj
python tools/vsbuild.py src/NekoLua/NekoLua.csproj -p:Configuration=Release -p:Platform=x64

# 可选 Astra 桥接（需 AstraFramework 6.1.0 的只读 DLL 位于 libs/astra/6.1.0/）
dotnet restore src/NekoLua.Astra/NekoLua.Astra.csproj
python tools/vsbuild.py src/NekoLua.Astra/NekoLua.Astra.csproj -p:Configuration=Release -p:Platform=x64
```

产物：

- `artifacts/build/NekoLua/x64/Release/net481/NekoLua.dll`
- `artifacts/build/NekoLua.Astra/x64/Release/net481/NekoLua.Astra.dll`

> `net481` + 公开化程序集必须走 `tools/vsbuild.py`；`dotnet build` 会报 `MSB4803`。

## 扩展开发

第三方能力通过 `NekoLua/extensions/*.dll` 接入，不修改核心：

| 契约 | 用途 |
|---|---|
| `NekoLua.Registers.IRegisterLua` | 新增 Lua 全局表（`Name` 全局唯一，重名拒绝） |
| `NekoLua.Loader.ILuaExtension` | 生命周期回调（`OnLoad` / `OnUnload`） |
| `NekoLua.Dependencies.LuaCleanupRegistry` | 按 mod 登记清理动作，随 `reload` / `refresh` / `Disable` 回收 |

扩展在 `main.lua` 执行前完成注册与 `OnLoad`；`Disable` 时逆序 `OnUnload`。详见
[docs/EXTENSIONS.md](docs/EXTENSIONS.md)。

## 文档

| 文档 | 内容 |
|---|---|
| [docs/API.md](docs/API.md) | Lua 全局表与 C# 扩展契约（73 张表，自动生成） |
| [docs/API-ASTRA.md](docs/API-ASTRA.md) | Astra 桥接 Lua API（team / role / item / wave / scheduler / position / hint / audio / countdown / effect） |
| [docs/EXTENSIONS.md](docs/EXTENSIONS.md) | 扩展 DLL 的编写、装载与回收 |
| [docs/ASTRA-EXTENSION.md](docs/ASTRA-EXTENSION.md) | Astra 桥接设计与装载时序 |
| [src/NekoLua/FLOW.md](src/NekoLua/FLOW.md) | 核心加载流程与执行模型 |
| [tests/lua/README.md](tests/lua/README.md) | Lua 冒烟测试套件（运行时 / Astra / 调度器 / 核心 API / 热重载） |

## 测试

`tests/lua/` 是一组可直接部署到 `NekoLua/mods/` 的测试插件，附带 `install.ps1`：

```powershell
powershell -File tests/lua/install.ps1 -NekoLuaRoot "<...>\SecretLab\NekoLua"
```

随后在控制台执行 `lua_rt`、`lua_astra`、`lua_sched`、`lua_core`、`lua_hr`、`lua_err`。
Astra 套件需要 `NekoLua.Astra.dll` 位于 `extensions/` 且 AstraFramework >= 6.1.0。

## 仓库结构

```text
src/NekoLua/          插件源码（核心，不引用任何其它插件工程）
src/NekoLua.Astra/    可选桥接工程（AstraFramework -> Lua）
libs/                 只读依赖（游戏、LabAPI、Astra 程序集）
tools/vsbuild.py      构建入口（net481 必需）
tools/gen-api-doc.ps1 从源码注释生成 docs/API.md
tests/lua/            Lua 测试套件
docs/                 文档
artifacts/            构建产物（不提交）
```

## 许可

MIT License，见 [LICENSE](LICENSE)。
