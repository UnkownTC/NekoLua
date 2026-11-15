# NekoLua 测试套件（Lua 插件）

一组可直接部署到服务器 `NekoLua/mods/` 的测试用 Lua 插件，覆盖：

| 编号 | 插件 | 覆盖内容 | 命令 |
|---|---|---|---|
| 00 | `00_runtime` | nekolua / player / log / storage / json / eventbus / eventhook / timer / command | `lua_rt` |
| 10 | `10_astra_content` | Astra 阵营/角色/波次定义与查询、give/get/remove、spawn、hint/audio/countdown/effect、`eventhook astraplayer` | `lua_astra` |
| 20 | `20_astra_errors` | 定义字段校验、未知波次、无玩家、重复 Id、未知枚举等负向路径 | `lua_err` |
| 30 | `30_astra_scheduler` | `astra.wave.scheduler()`：权重排序、人数门槛跳过、剩余数量、支援池 | `lua_sched` |
| 40 | `40_core_api` | base64 / csv / regex / crypto / buffer / meta / encoding / bit / random / datetime / version / uuid / vector3 / mathx / template / try / pool | `lua_core` |
| 50 | `50_hotreload` | `nekolua reload` 后的 timer / eventhook / eventbus / Astra 内容回收 | `lua_hr` |

Astra 相关插件需要：`NekoLua.Astra.dll` 在 `NekoLua/extensions/`，AstraFramework >= 4.0.0（低于此版本桥接会直接 `astra.available=false`）。

---

## 1. 安装

### 方式 A：脚本（推荐）

```powershell
# NekoLuaRoot = 服务端 NekoLua 目录（下面有 mods/ dependencies/ configs/ extensions/）
powershell -File tests/lua/install.ps1 -NekoLuaRoot "D:\...\SCP Secret Laboratory Dedicated Server\...\NekoLua"

# 只装到指定端口目录（默认装到 mods/global，对所有端口生效）
powershell -File tests/lua/install.ps1 -NekoLuaRoot "<...>\NekoLua" -Scope port -Port 7777
```

脚本会：

1. 复制 `dependencies/global/neko_testkit.lua`（测试公共库）；
2. 复制 `mods/<编号>` 到 `<NekoLuaRoot>/mods/global/`（或指定端口目录）。

### 方式 B：手动

```
NekoLua/
  dependencies/global/neko_testkit.lua
  mods/global/00_runtime/main.lua
  mods/global/10_astra_content/main.lua
  ...
```

然后执行 `nekolua refresh`（或重启服务器）。

---

## 2. 运行

在服务器控制台或 RA 中执行对应命令（与 `nekolua` 一样是控制台/RA 双端）：

```
lua_rt       # 运行时冒烟
lua_astra    # Astra 内容
lua_err      # 负向测试
lua_sched    # 调度器
lua_core     # 核心 API
lua_hr       # 热重载
```

预期：

- 每条 PASS 出现在服务器控制台（`[LuaMod-<mod>]` 前缀）；
- 最后一行是汇总：`<suite>：通过 N / 失败 0 / 跳过 M`；
- `10_astra_content` 在没有在线玩家时会有 3 条 SKIP，属于正常；
- `lua_err` 里的"红色日志"是测试主动触发的异常被 `try.attempt` 捕获，最终汇总应为失败 0。

热重载验证：

```
lua_hr
nekolua reload 50_hotreload
lua_hr
```

期望第二次 `lua_hr`：`load 计数增长` PASS，且 `timer 数量`、`eventhook 数量`、`hr_role` / `hr_wave` 数量仍为 1（说明 `CleanupMod` 生效、Astra 内容按 Id 替换而非重复注册）。

---

## 3. 覆盖边界

- 覆盖的是**运行时契约**（加载、命令、事件、定时器、序列化、Astra 桥接、热重载），不逐条断言 73 张全局表的所有成员。
- 网络 / 文件 / 进程 / Win32 / 音频文件等需要真实环境或外部资源的 API 只做了"缺失资源不崩"的冒烟，未做完整功能测试。
- 需要游戏实体的断言（给玩家套角色、提示、效果）在无玩家时自动 SKIP，需进服后重跑 `lua_astra`。

> **沙箱注意**：NekoLua 的 MoonSharp `Preset_HardSandbox` **不提供 `pcall` / `xpcall`**。
> 测试库统一用 NekoLua 的 `try.attempt(fn)` 捕获异常（它会把异常转成 `{message, trace, type}` 表）。
> 如果你在自己的脚本里写了 `pcall`，会得到 `attempt to call a nil value`。
>
> **尾调用限制**：MoonSharp 2.0 对 `return f(a, ..., try.attempt(fn))`（尾调用 + 多返回值展开）解析有缺陷，
> 会误报 `attempt to call a string value`。测试库统一先 `local ok, err = try.attempt(fn)` 再转发；
> 自己写辅助函数时也不要图省事写成尾调用。
>
> **定位崩溃**：`10_astra_content` 每个步骤前会 `kit.trace(...)` 写 `mods/global/10_astra_content/trace.txt`；
> 失败时先看最后一行标记，定位到具体调用。
>
> **调用风格**：NekoLua 的代理函数（`buffer.*`、`pool.*` 等）是**表字段函数**，不是带 `self` 的方法，
> 统一用点号调用（`buf.writeByte(65)`、`p.release(obj)`）；用冒号会把表自己当成第一个参数。
> 只有测试库自己的 `s:eq(...)` 这类是 Lua 方法，用冒号。

---

## 4. 卸载

删除 `mods/global/` 下这 6 个目录与 `dependencies/global/neko_testkit.lua`，然后 `nekolua refresh`。
