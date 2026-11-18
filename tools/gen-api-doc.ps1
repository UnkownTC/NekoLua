# 生成 docs/API.md 的全局表参考部分。
# 数据来源：src/NekoLua/Proxies/**/*.cs 中各 IRegisterLua 的 Name、类 <summary>、t["key"] 赋值。
# 用法：pwsh tools/gen-api-doc.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proxies = Join-Path $root 'src\NekoLua\Proxies'

$folders = @(
    @{ Dir = ''; Title = '核心运行时表'; Desc = '与游戏无关的基础设施：异步、定时器、事件、序列化、加解密、进程与原生互操作等。' },
    @{ Dir = 'GameContents'; Title = '游戏内容表'; Desc = '玩家、物品、门、房间、回合、广播等游戏对象与全服能力；多数入口受 `LuaGlobalApiGuard` 标签门控。' },
    @{ Dir = 'IO'; Title = '文件与 IO 表'; Desc = '受 `LuaPathGuard` 约束的路径、文件、目录与压缩包操作。' },
    @{ Dir = 'Net'; Title = '网络表'; Desc = 'HTTP、TCP、UDP、WebSocket、FTP；受 `LuaNetworkGuard` 与配置开关约束。' },
    @{ Dir = 'Serialization'; Title = '序列化表'; Desc = 'base64 / csv / json / xml / yaml。' },
    @{ Dir = 'Interop'; Title = '原生互操作表'; Desc = 'Win32 与 IntPtr；需要 `Config.EnableWin32` 并受 DLL/函数白名单约束。' },
    @{ Dir = 'UnityContents'; Title = 'Unity 数据表'; Desc = '向量、四元数与时间。' },
    @{ Dir = 'SubLibs'; Title = '子库封装表'; Desc = '对随服务端加载的第三方库的薄封装。' }
)

function Get-Summary([string] $text) {
    $m = [regex]::Match($text, '(?s)/// <summary>(.*?)/// </summary>')
    if (-not $m.Success) { return '' }
    $lines = $m.Groups[1].Value -split "`n" | ForEach-Object {
        $s = $_ -replace '^\s*///\s?', ''
        $s = $s -replace '<c>([^<]*)</c>', '`$1`'
        $s = $s -replace '<see cref="([^"]+)"\s*/>', '`$1`'
        $s = $s -replace '<paramref name="([^"]+)"\s*/>', '`$1`'
        $s = $s -replace '<typeparamref name="([^"]+)"\s*/>', '`$1`'
        $s = $s -replace '<see langword="([^"]+)"\s*/>', '`$1`'
        $s = $s -replace '<[^>]+>', ''
        $s.TrimEnd()
    }
    while ($lines.Count -gt 0 -and $lines[0].Trim() -eq '') { $lines = $lines[1..($lines.Count - 1)] }
    while ($lines.Count -gt 0 -and $lines[-1].Trim() -eq '') { $lines = $lines[0..($lines.Count - 2)] }
    return ($lines -join "`n")
}

$all = @()
foreach ($folder in $folders) {
    $dir = if ($folder.Dir) { Join-Path $proxies $folder.Dir } else { $proxies }
    $files = Get-ChildItem -LiteralPath $dir -File -Filter '*.cs'
    foreach ($file in $files) {
        $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8
        if ($text -notmatch 'IRegisterLua') { continue }
        $nameMatch = [regex]::Match($text, 'Name[^\n]{0,40}?=\s*"([^"]+)"')
        if (-not $nameMatch.Success) { throw "无法解析 Name: $($file.FullName)" }
        $classMatch = [regex]::Match($text, 'public class (\w+)\s*:\s*IRegisterLua')
        $keys = [regex]::Matches($text, '\[\s*"([A-Za-z_][A-Za-z0-9_.]*)"\s*\]\s*=') |
            ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique
        $summary = Get-Summary $text
        $purpose = ''
        foreach ($line in ($summary -split "`n")) {
            if ($line.Trim() -eq '') { continue }
            $candidate = $line -replace '^\s*\w+\s*表\s*[:：]\s*', ''
            $candidate = $candidate.TrimEnd(':', '：', ' ').Trim()
            if ($candidate -ne '') { $purpose = $candidate; break }
        }
        if ($purpose.Length -gt 80) { $purpose = $purpose.Substring(0, 80) + '…' }

        $all += [pscustomobject]@{
            Folder  = $folder.Dir
            Title   = $folder.Title
            File    = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
            Name    = $nameMatch.Groups[1].Value
            Class   = $classMatch.Groups[1].Value
            Keys    = ($keys -join ', ')
            Summary = $summary
            Purpose = $purpose
        }
    }
}

$sb = [System.Text.StringBuilder]::new()
function Add([string] $text) { [void] $sb.AppendLine($text) }

# ---------- 头部 ----------
Add '# NekoLua Lua API 参考'
Add ''
Add '> 适用版本：以仓库当前 `main` 为准；Astra 桥接基线 AstraFramework **4.0.0**，Lua 运行时 MoonSharp **2.0.0**。'
Add '> 本文只描述脚本可见面（Lua 全局表、对象成员与门控），C# 扩展契约见文末。'
Add '> 相关文档：[README](../README.md) · [运行流程](../src/NekoLua/FLOW.md) · [C# 扩展](EXTENSIONS.md) · [Astra 桥接设计](ASTRA-EXTENSION.md) · [Astra 桥接 API](API-ASTRA.md)'
Add ''
Add '---'
Add ''
Add '## 1. 运行模型'
Add ''
Add '### 1.1 目录与入口'
Add ''
Add '服务端 `SecretLab/NekoLua/` 下按端口隔离，`global` 子目录对所有端口生效：'
Add ''
Add '```text'
Add 'SecretLab/NekoLua/'
Add '  mods/<port>/<mod>/main.lua      端口专属 mod（入口固定为 main.lua）'
Add '  mods/global/<mod>/main.lua      全局 mod'
Add '  dependencies/<port>/ | global/  Lua 依赖（dofile/require 可见范围由路径守卫约束）'
Add '  configs/<port>/<mod>.json       mod.registercfg() 落盘位置'
Add '  extensions/*.dll                桥接扩展程序集（如 NekoLua.Astra.dll）'
Add '```'
Add ''
Add '- 每个 mod 目录下必须有 `main.lua`，缺失时只告警并跳过。'
Add '- `require("a.b")` 解析为 mod 目录内的相对路径 `a/b.lua`；禁止越出 mod 目录与依赖目录。'
Add '- 脚本以 MoonSharp `Preset_HardSandbox` 执行：没有 `io` / `os` / `debug` / `package` / `load` / `loadfile` / `dofile`；需要的能力必须走代理表。'
Add ''
Add '### 1.2 启动顺序'
Add ''
Add '```text'
Add 'MainPlugin.Enable()'
Add '  LuaEmbeddedExtractor.ExtractAll()      释放内置 Lua 依赖'
Add '  LuaRegister.Clear(); Register(core)    注册核心全局表'
Add '  LuaExtensionLoader.LoadAll()           扫描 extensions/*.dll，注册扩展表并调用 ILuaExtension.OnLoad'
Add '  LuaGameObject.Create()                 启动每帧驱动（timer/scheduler/async/main-thread 队列）'
Add '  LuaLoader.LoadAll()                    扫描 mods 目录并执行 main.lua'
Add '```'
Add ''
Add '- 扩展在脚本执行**之前**完成注册与 `OnLoad`，因此 `main.lua` 一执行就能看到扩展表。'
Add '- `Disable()` 逆序调用 `ILuaExtension.OnUnload`，并清空 timer / eventhook / command / eventbus / network / scheduler / regex / win32 / 主线程队列。'
Add '- 扩展 DLL 用 `Assembly.Load(byte[])` 装载：运行期可以覆盖文件，但新代码要重启服务端才生效；扩展之间不能互相作为编译依赖。'
Add ''
Add '### 1.3 表隔离与热重载'
Add ''
Add '- 全局表不是共享单例：每个 mod 调用一次 `IRegisterLua.Create(mod)`，拿到属于自己的表实例；闭包中的状态天然按 mod 隔离。'
Add '- 同名表注册一律拒绝（忽略大小写），不会静默覆盖；冲突会打印 `Logger.Error`，先注册者生效。'
Add '- `nekolua reload` 会重建 mod 的 Script；`storage` 明确跨热重载保留，`timer` / `eventhook` / `command` / `eventbus` / `scheduler` 的注册会按 mod 回收。'
Add ''
Add '## 2. 调用约定'
Add ''
Add '### 2.1 表函数用点号，对象方法用冒号'
Add ''
Add '```lua'
Add 'timer.delay(1.0, function() end)        -- 表字段函数：点号'
Add 'local e = elevator.list()[1]             -- 返回托管对象句柄'
Add 'e:setDestination(1)                      -- 对象 public 方法：冒号'
Add 'print(e.isReady)                         -- 对象 public 属性/字段：点号'
Add '```'
Add ''
Add '`buffer.*`、`pool.*` 这类表同样是「字段函数」，不要写成 `buf:writeByte(65)`——冒号会把表自身当第一个实参。'
Add ''
Add '### 2.2 对象句柄'
Add ''
Add '- 游戏对象（`player` / `item` / `door` / `room` …）与 Astra 内容对象通过 `LuaObjectProxy` 暴露：'
Add '  - `obj.name` 读取 public 属性或字段，`obj.name = v` 写属性/字段；'
Add '  - `obj:method(a, b)` 调用 public 实例方法；'
Add '  - 成员名大小写不敏感；索引器与泛型方法不可见；'
Add '  - 方法内抛出的异常会转成 Lua 运行时错误，不会把 .NET 异常栈直接泄进脚本。'
Add '- 句柄身份由 `LuaObjectCache` 统一管理，同一托管对象在同一 mod 内拿到同一张表；对象被游戏销毁后旧句柄会失效。'
Add '- **守卫边界**：`LuaGlobalApiGuard` 只覆盖代理里显式登记的全服级入口；经由 `LuaObjectProxy` 反射出来的实例成员（例如 `door:destroy()`）不在门控内，由脚本审计与对象生命周期负责。'
Add ''
Add '### 2.3 错误与异常'
Add ''
Add '```lua'
Add '-- 会抛异常：catch 缺省时向上传播'
Add 'try.run(function() error("boom") end, function(err) print(err.message) end)'
Add ''
Add '-- 不抛异常：返回 (true, ...) 或 (false, errTable)'
Add 'local ok, err = try.attempt(function() return risky() end)'
Add 'if not ok then print(err.type, err.message, err.trace) end'
Add '```'
Add ''
Add '- 代理层统一把参数错误抛成 `ScriptRuntimeException`，信息里带 API 签名，例如 `astra.role.give(ply, id): 需要一个非空字符串`。'
Add '- 第三方库抛出的 .NET 异常如果直接穿过 C# 回调边界，会破坏 MoonSharp 执行栈（表现为之后的任意 `local` 赋值崩溃）。桥接层已将 Astra 调用包进 `Guarded(...)`；自定义扩展也应在回调边界自行捕获并转成 `ScriptRuntimeException`。'
Add ''
Add '### 2.4 线程与调度'
Add ''
Add '- 脚本入口与每帧驱动都在主线程：`timer` / `scheduler` / `async` / `eventhook` 的回调默认在主线程执行。'
Add '- 网络等异步完成事件由 `LuaMainThread` 队列调度回主线程；队列上限 `MainThreadQueueLimit`，每帧最多处理 `MainThreadTasksPerTick` 条。'
Add '- Astra 桥接的写操作（`define` / `give` / `remove` / `wave.spawn` / `scheduler` 全部方法 / hint/audio/countdown/effect）强制校验主线程，非主线程调用会直接报错。'
Add ''
Add '## 3. 全局表索引'
Add ''
Add ('共 **' + $all.Count + '** 张表。每张表都按 mod 实例化，下表为「表名 → 实现类」索引。')
Add ''
$current = $null
foreach ($item in $all) {
    if ($item.Folder -ne $current) {
        $current = $item.Folder
        Add ''
        Add ('**' + $item.Title + '**')
        Add ''
        Add '| 表 | 实现 | 作用 |'
        Add '|---|---|---|'
    }
    Add ('| `' + $item.Name + '` | `' + $item.Class + '` | ' + ($item.Purpose -replace '\|', '\|') + ' |')
}
Add ''

# ---------- 参考正文 ----------
$index = 4
foreach ($folder in $folders) {
    Add ('## ' + $index + '. ' + $folder.Title)
    Add ''
    Add $folder.Desc
    Add ''
    $items = $all | Where-Object { $_.Folder -eq $folder.Dir } | Sort-Object Name
    foreach ($item in $items) {
        Add ('### `' + $item.Name + '`')
        Add ''
        Add ('> 实现：`' + $item.Class + '` · 源码：`' + $item.File + '`')
        Add ''
        if ($item.Summary) {
            Add '```text'
            foreach ($line in ($item.Summary -split "`n")) { Add $line }
            Add '```'
        }
        else {
            Add '_该类未提供 summary，请直接阅读源码。_'
        }
        Add ''
    }
    $index++
}

# ---------- 指令 ----------
Add ('## ' + $index + '. RA / 控制台指令')
Add ''
Add '父命令 `nekolua`，同时注册到 `RemoteAdminCommandHandler` 与 `GameConsoleCommandHandler`。'
Add ''
Add '| 子命令 | 别名 | 说明 |'
Add '|---|---|---|'
Add '| `nekolua list` | `l`, `ls` | 列出已加载 mod、禁用状态与目录 |'
Add '| `nekolua reload [mod…]` | `r`, `rl` | 无参数重载全部启用 mod；指定名字只重载匹配项（忽略大小写） |'
Add '| `nekolua refresh` | `rf`, `scan` | 重新扫描 mods 目录，加载新增、卸载已删除 |'
Add ''
Add '三个子命令都要求 `PlayerPermissions.ServerConsoleCommands`；异常详情只写服务端控制台，对发送者返回固定话术。'
Add ''

# ---------- 配置 ----------
Add ('## ' + ($index + 1) + '. 配置项')
Add ''
Add '配置文件由 LabAPI 加载，字段与默认值如下（`src/NekoLua/Config.cs`）。'
Add ''
Add '| 字段 | 默认值 | 说明 |'
Add '|---|---|---|'
Add '| `ExtractEmbeddedLua` | `true` | 启动时释放内置 Lua 依赖 |'
Add '| `OverwriteEmbeddedLua` | `false` | 释放时覆盖已存在的同名文件 |'
Add '| `MainThreadQueueLimit` | `4096` | 主线程队列上限 |'
Add '| `MainThreadTasksPerTick` | `64` | 每帧最多处理的主线程任务数 |'
Add '| `HttpTimeoutSeconds` | `15` | HTTP 请求超时（秒） |'
Add '| `HttpMaxConcurrency` | `16` | 单次 HTTP 并发上限 |'
Add '| `EnableFtp` | `false` | 是否允许 `ftp` 表 |'
Add '| `BlockedPaths` | 系统目录默认表 | 路径守卫黑名单 |'
Add '| `AllowedPaths` | `[]` | 路径守卫白名单（优先于黑名单） |'
Add '| `EnvWriteEnabled` | `false` | 是否允许 `env.set` / `env.delete` |'
Add '| `TimerMaxConsecutiveErrors` | `5` | 定时器连续异常多少次后自动注销 |'
Add '| `EnableNetwork` | `true` | 网络总开关 |'
Add '| `BlockPrivateNetworkTargets` | `true` | 禁止访问内网/回环地址 |'
Add '| `MaxConnectionsPerMod` | `16` | 每个 mod 的连接数上限 |'
Add '| `EnableProcess` | `false` | 是否允许 `process` 表 |'
Add '| `ProcessWhitelist` | `[]` | 允许启动的进程名 |'
Add '| `ProcessKillBlacklist` | 系统关键进程 | 禁止结束的进程名 |'
Add '| `ProcessKillSelfForbidden` | `true` | 禁止结束服务端自身进程 |'
Add '| `EnableWin32` | `false` | 是否允许 `win32` 表 |'
Add '| `Win32DllWhitelist` | `kernel32`, `user32`, `shell32`, `gdi32`, `advapi32`, `ntdll` | 允许加载的 DLL |'
Add '| `Win32FunctionWhitelist` | `[]` | 非空时只允许列出的函数 |'
Add '| `Win32FunctionBlacklist` | 进程/内存/远程线程等危险函数 | 始终拒绝的函数（通配符匹配） |'
Add '| `Win32AllowPointerArgs` | `false` | 是否允许传指针参数 |'
Add '| `Win32IntPtrAsTable` | `true` | 返回值指针是否以可读表形式暴露 |'
Add '| `EnableNvorbis` | `true` | 是否允许 `nvorbis` 表 |'
Add '| `NvorbisMaxSamples` | `10000000` | 单文件解码采样上限 |'
Add '| `AllowScriptRunCommand` | `false` | 是否允许脚本执行服务端控制台命令 |'
Add '| `RunCommandPrefixWhitelist` | `[]` | 允许执行的命令前缀 |'
Add ('| `DeniedGlobalApis` | `[]` | 关闭的全服级 API 标签，见 §' + ($index + 2) + '.3 |')
Add ''
Add ('## ' + ($index + 2) + '. C# 扩展 API')
Add ''
Add ('### ' + ($index + 2) + '.1 `IRegisterLua` — 新增一张全局表')
Add ''
Add '```csharp'
Add 'public interface IRegisterLua'
Add '{'
Add '    string Name { get; }                    // Lua 全局名，全局唯一（忽略大小写）'
Add '    Table Create(LuaModInstance mod);       // 每个 mod 调用一次，返回该 mod 专属表'
Add '}'
Add '```'
Add ''
Add '- 放进 `SecretLab/NekoLua/extensions/` 的 DLL 会被反射扫描；类型必须是非抽象类、有公开无参构造函数。'
Add '- 也可以进程内直接调用 `LuaRegister.RegisterInstance(new MyTable())`；重名/空名返回 `false` 并记日志。'
Add '- 不要在 `Create` 里访问游戏对象：注册时服务端可能尚未就绪，把入口留到脚本调用或 `ILuaExtension.OnLoad` 之后。'
Add ''
Add ('### ' + ($index + 2) + '.2 `ILuaExtension` — 生命周期')
Add ''
Add '```csharp'
Add 'public interface ILuaExtension'
Add '{'
Add '    string Id { get; }   // 扩展标识，重复则后者被跳过'
Add '    void OnLoad();       // 主线程，LuaLoader.LoadAll 之前'
Add '    void OnUnload();     // 主线程，逆序调用'
Add '}'
Add '```'
Add ''
Add '- 程序集无法卸载（net481 无 ALC），`OnLoad` 必须幂等，`OnUnload` 必须清干净静态状态。'
Add '- 扩展只应依赖 NekoLua 公开 API；把扩展当 LabAPI 插件注册会被 Astra 的自动扫描误收，桥接因此采用「普通 DLL + extensions 目录」形态。'
Add ''
Add ('### ' + ($index + 2) + '.3 全服级 API 门控')
Add ''
Add '```csharp'
Add 'LuaGlobalApiGuard.EnsureAllowed("warhead", "warhead.detonate()");'
Add '```'
Add ''
Add '可用标签：`warhead` `round` `server` `whitelist` `reservedSlots` `permissions` `broadcast` `cassie` `decontamination` `map` `hazard` `pocket` `scp914`；Astra 桥接新增 `astra` `astra.role` `astra.item` `astra.team` `astra.wave` `astra.position`。'
Add ''
Add '扩展在自己的全服级入口调用 `EnsureAllowed(tag, api)`；标签由运营者填进 `Config.DeniedGlobalApis`。'
Add ''
Add ('### ' + ($index + 2) + '.4 其它公开钩子')
Add ''
Add '| API | 用途 |'
Add '|---|---|'
Add '| `LuaEventHook.RegisterEventClass(name, type)` | 给 `eventhook.bind` 增加一个事件类（类内 public 事件可被脚本绑定） |'
Add '| `LuaCleanupRegistry.Register(mod, action)` | 注册随 mod 重载/卸载执行的清理动作 |'
Add '| `LuaObjectCache.Wrap(obj)`（实例方法） | 复用托管对象缓存，避免 Lua GC 与 Unity 生命周期冲突 |'
Add '| `LuaPaths.Extensions` | `extensions/` 目录，扩展装载器扫描目标 |'
Add ''
Add ('## ' + ($index + 3) + '. 已知边界与版本基线')
Add ''
Add '- 核心表由 `LuaRegister.Register(PluginAssembly)` 反射扫描；`extensions/` 的装载逐 DLL、逐类型隔离（加载/实例化/`OnLoad` 失败只记日志并继续），核心程序集自身的构造函数异常仍会中断启用。'
Add '- 同一个扩展 DLL 在一次启用中只会被装载一次；覆盖文件后必须重启才换新代码，且扩展之间不能互相引用。'
Add '- `LuaObjectProxy` 反射出的实例成员不受全服级门控覆盖，这是刻意的边界：新增全服破坏性能力时应加显式代理入口，而不是依赖反射。'
Add '- MoonSharp 2.0.0 对「尾调用 + 多返回值展开」有解析缺陷：`return f(a, try.attempt(fn))` 可能报 `attempt to call a string value`。把 `try.attempt` 的结果先落到 `local` 再使用。'
Add '- 本文的「全局表」章节由 `tools/gen-api-doc.ps1` 从源码 `<summary>` 生成；改代理注释后请重新生成并提交。'
Add ''

$out = Join-Path $root 'docs\API.md'
[System.IO.File]::WriteAllText($out, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Host "written $out ($($sb.Length) chars)"
