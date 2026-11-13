# libs/ — 工作区共享依赖文件

存放**多个项目共同引用**的依赖文件：参考程序集、第三方 DLL、NuGet 离线包（`.nupkg`）、平台 SDK 程序集等。

## 约定

| 项 | 规则 |
|---|---|
| 性质 | **只读参考资源**；禁止修改、删除、重命名其中的任何文件 |
| 用途 | 供项目通过 `<Reference>` / `HintPath` / 本地 NuGet 源引用 |
| 目录组织 | `libs/<分类>/<版本>/`：按用途分目录，版本取自程序集真实 `AssemblyVersion`/`FileVersion`；游戏与 Unity 引擎程序集未嵌入版本号，统一以 `current` 标注（例：`libs/labapi/1.1.7/`、`libs/harmony/2.4.2/`、`libs/scpsl-server/current/`）。引用走 `<Reference HintPath="libs/<分类>/<版本>/<名>.dll" />` |
| 引入流程 | 属**共享资源变更**，须走 `docs/git-workflow-policy.md` §4（集成者唯一写者），任务分支不得自行添加/替换 |
| 版本记录 | 新增依赖必须在本文件下方登记表中记录来源、版本、许可、用途 |
| 入库 | 本目录管理非 NuGet 二进制与显式离线包，均须进入版本控制；普通 `PackageReference` 可使用 NuGet 全局缓存或配置源恢复，无需复制包二进制到本目录 |
| 体积 | 可从包源恢复的大型依赖优先使用版本受控的 `PackageReference`；确需提交的 libs 文件 >10 MiB 时打包为离线 `.nupkg` 放入 `libs/nupkgs/` |

> NuGet 缓存例外只适用于 `PackageReference`。禁止在 `.csproj` 中用 HintPath 直接引用
> `%USERPROFILE%/.nuget/packages`、SDK 私有目录或其他本机路径。包 ID 与精确版本必须由
> `Directory.Packages.props`、合规的 `VersionOverride` 或锁文件约束；私有源、下架包或明确要求
> 离线构建且无法在干净计算机恢复时，才将 `.nupkg` 标准入库到 `libs/nupkgs/`。

## 本地 NuGet 源（仅离线/私有包）

```bash
dotnet nuget add source "D:\Userdata\Documents\AI-Workspace\libs\nupkgs" -n workspace-libs
```

或在根目录 `nuget.config` 中配置 `<packageSources>`（`nuget.config` 属共享文件）。

## 公开化依赖（PublicizedAssemblies）

部分只读参考程序集（如 `Assembly-CSharp`）需被补丁项目以**非公开成员**方式访问时，经 `AssemblyPublicizer` 公开化后存入本子目录，供多个项目共享引用：

- 路径：`libs/PublicizedAssemblies/`，公开化 DLL 扁平存放为 `<原名>-Publicized.dll`（如 `Assembly-CSharp-Publicized.dll`）
- 内容：原参考 DLL 仍留在 `libs/` 对应分类（只读）；本目录只存公开化产物 `<原名>-Publicized.dll` + 本目录 `README.md`（来源版本、Publicizer 版本、生成命令）
- 性质：**只读、入库**；变更 = 共享资源变更，交集成者
- 引用方式：补丁项目通过 `<Reference HintPath="libs/PublicizedAssemblies/<原名>-Publicized.dll" />` 引用（详见 `docs/dotnet-coding-standard.md` §9.8.4 模式 B）
- 与原版同步：上游参考 DLL 升级时须重新公开化并标注版本，避免与原版行为分叉

## 依赖登记表

| 分类/目录 | 名称 | 版本 | 来源 | 许可 | 用途 | 登记人/日期 |
|---|---|---|---|---|---|---|
| `libs/scpsl-server/current/` | 游戏/Unity 服务器端程序集（Assembly-CSharp*、NorthwoodLib、Mirror*、CommandSystem.Core、CustomPasses、Snake、Caress、Interop、Pooling*、Facepunch.Steamworks.Win64） | current（程序集未嵌入版本号） | SCP:SL 服务器 / 官方 | 随上游 | 插件运行与补丁目标 | 用户 2026-09-25 |
| `libs/unity/current/` | Unity 引擎模块（UnityEngine.*、Unity.*） | current | Unity 官方 | Unity 许可 | 引擎 API 引用 | 用户 2026-09-25 |
| `libs/framework/current/` | .NET 框架参考程序集（mscorlib、netstandard、System.*、Microsoft.Bcl.*，含 System.ComponentModel.DataAnnotations）+ Windows.winmd（Windows SDK 随附的 WinRT 元数据，供插件声明的 Windows 运行时类型引用） | current | .NET / Unity / Windows SDK 随附 | MIT / 微软 | 框架 BCL + WinRT 互操作 | 用户 2026-09-25 |
| `libs/labapi/1.1.7/` | LabAPI | 1.1.7 | 官方发行 | 见 `vendor/LabAPI` LICENSE | 插件框架参考与编译 | 用户 2026-09-25 |
| `libs/harmony/2.4.2/` | Harmony（HarmonyX） | 2.4.2 | NuGet / 上游 | MIT | 运行时补丁框架 | 用户 2026-09-25 |
| `libs/exiled/9.14.2/` | EXILED 框架（Exiled.API / Exiled.Events / Exiled.Loader / Exiled.Permissions / Exiled.CreditTags / Exiled.CustomItems / Exiled.CustomRoles）+ 相关依赖（Mono.Posix 4.0.0.0、SemanticVersioning 2.0.2） | 9.14.2（Exiled.* 程序集；卫星依赖版本见条目名） | EXILED 官方发行 | 见 `vendor/EXILED` LICENSE | SCP:SL 插件框架参考与编译 | 用户 2026-09-25 |
| `libs/audio/current/` | 音频（NAudio.*、SecretLabNAudio.*、NVorbis、NLayer*） | current | NuGet / 上游 | 随上游 | 音频处理 | 用户 2026-09-25 |
| `libs/net-crypto/current/` | 网络/加密/工具（DnsClient、BouncyCastle.Cryptography、Mono.Security、YamlDotNet、zxing） | current | NuGet / 上游 | 随上游 | 网络/加密/序列化 | 用户 2026-09-25 |
| `libs/PublicizedAssemblies/` | 公开化程序集（Assembly-CSharp、Assembly-CSharp-firstpass、CommandSystem.Core、HintServiceMeow、LabApi、Mirror 的 `-Publicized.dll`） | 须与原参考 DLL 版本一致（HintServiceMeow 为 6.0.0.0） | 由 `libs/` 已登记参考 DLL / `vendor/HintServiceMeow` 经 `Krafs.Publicizer` 生成 | 同原 DLL 许可 | 补丁项目调用非公开成员（禁反射） | 用户 2026-09-25；HintServiceMeow 条目 task-212 2026-10-01 |
| `libs/hintservicemeow/6.0.0.0/` | HintServiceMeow | 6.0.0.0 | 工作区 `vendor/HintServiceMeow`（DevMeow 分支）构建产物，其 `AssemblyVersion`/`FileVersion`/`Version` 均为 `6.0.0.0`（二进制串校验无 `5.5.0`/`5.5.1` 痕迹，排除旧 NuGet 5.5.0 副本） | MIT（Copyright (c) 2024 MeowServer~，见 `vendor/HintServiceMeow/LICENSE`） | SCP:SL 提示服务（Hint 解析 / 坐标工具）；工作区主引用为 `vendor` 源码 `ProjectReference` + `Krafs.Publicizer`，本二进制为同一版本的只读缓存 / 离线引用 | WorkBuddy（agent）2026-09-25 |
| `libs/hintservicemeow/5.5.1.0/` | HintServiceMeow-LabAPI.dll（官方 5.5.1 发行版 LabAPI 构建 flavor，177,664 B，SHA256 `5dbd4a2b…`；与 6.0.0.0 目录的 vendor DevMeow 构建**并存不同基线**） | FileVersion/ProductVersion `5.5.1.0`（构建元数据 commit `2bee675f…`）；AssemblyVersion `5.5.0.0`（与上游 packages 的 `HintServiceMeow.5.5.0` NuGet 引用版本一致） | 用户投递（2026-10-05），构建元数据指向 MeowServer/HintServiceMeow 官方仓库 | MIT（Copyright (c) 2024 MeowServer~，同 `vendor/HintServiceMeow/LICENSE`） | `src/Site19ServerPluginEX` 的 HUD 提示依赖：EX 上游 V1.5.5 按 5.5.x API 编译（HintAlignment/HintVerticalAlign/UISend 分区体系），不升 vendor 6.0.0.0 基线；引用见该工程 csproj（HintPath 锚定本目录） | WorkBuddy（agent）2026-10-05（task-212） |
| `libs/ddsite/`（**扁平分类**） | DiaoInterface 参考服务端 `deps/` 运维实配副本，70 个异构二进制：Harmony 系（0Harmony/Harmony）、LabAPI 系（LabApi.Helpers/Axwabo.Helpers/SteveFramework）、Exiled（Exiled.API）、微 ORM 与存储（LiteDB/MySql.Data/Dapper 邻接件/Polly/SharpCompress/PinYinConverterCore）、音频（NAudio.*/SecretLabNAudio.*/NVorbis/NLayer*/AMERTAudioModule/Audio*Api/SCPSLAudioApi）、SCP:SL 邻接件（Pooling.dll/PoolingLib.dll/ScpProximityChat.Core/RueI/Glicko2/SemanticVersioning）、序列化与协议（Newtonsoft.Json/Google.Protobuf/YamlDotNet/Ubiety.Dns.Core）、MonoMod/Mono.Cecil 工具链、System.* 兼容包（Buffers/Memory/Numerics.Vectors/Runtime.CompilerServices.Unsafe/Threading.Tasks.Extensions/ValueTuple）、参考部署的 `WebCommand-server-2026.dll`（原名 `WebCommand (5).dll`，与 `reveng/projects/webcommand/input/WebCommand-Server-2026.dll` 同哈希 c2e47e8f…），以及被运维停用的 `.disabled`/`.old` 副本（AudioPlayerApi.dll.disabled、ICSharpCode.SharpZipLib.dll.old、Newtonsoft.Json.dll.old、PinYinConverterCore.dll.old、Polly.dll.old） | 混合（逐文件 `AssemblyVersion` 见各 DLL；`0Harmony` 2.4.2、`Harmony` 为上游 2.x 分支、`Newtonsoft.Json` 13.0.x、`MySql.Data` 8.x） | 2026-10-04 用户投递的参考部署包 `IIS.zip` → `deps/`（SCP:SL 服务器运维实配目录快照）；`WebCommand-server-2026.dll` 由 `reveng/projects/webcommand` 取证自同一部署 | 随上游（内部业务代码 + 各开源库原许可） | ① 保留「该参考服务器实际装了哪一套版本」的集合证据（故整包扁平入库、不拆分）；② `reveng/projects/ddsite` 编译引用（`Pooling.dll` 与本工作区 `libs/scpsl-server/current/Pooling.dll` **不同构建**，必须取本目录副本） | WorkBuddy（agent）2026-10-04（task-212） |
| `libs/roslyn/10.0.401/`（**未入库**） | Roslyn 编译器 APIs：Microsoft.CodeAnalysis.dll（8,292,136 B）、Microsoft.CodeAnalysis.CSharp.dll（19,191,592 B） | 10.0.401 | .NET SDK 自带（`C:\Program Files\dotnet\sdk\<ver>\Roslyn\bincore\`），task-202 原 agent 就地复制到 `libs/roslyn/10.0.401/` | MIT（Roslyn / .NET Foundation） | 语法树 + 语义模型驱动的作用域感知改名（R9 符号还原，替代已证伪的文本启发式）；供 `reveng/projects/hmyhserver/tools` 下的工具引用 | WorkBuddy（agent）2026-09-26 |
| `libs/scpsl-plugins/1.0.0/` | 第三方服务器插件互操作参考程序集：AdminTools.dll（82,944 B，SHA256 `22e2bae9…`）、ProjectMER.dll（136,704 B，SHA256 `2091a7d4…`）、SCP-2928_ShaoMaoPlugin.dll（47,104 B，SHA256 `41cb81d5…`） | AdminTools 1.0.0.0 / ProjectMER 1.0.0.0 / SCP-2928_ShaoMaoPlugin 1.0.0.0（AssemblyVersion） | 上游仓库 `PancakeMeow/Site19ServerPluginEX`（V1.5.5 基线 `20bc81a`）携带的 `Libraries/` 副本（原为服务器插件目录快照），task-212 入库时登记 | 随上游（社区插件二进制） | `src/Site19ServerPluginEX` 跨插件 API 互操作编译引用（AdminTools.Commands / ProjectMER SchematicObject / SCP-2928 行为协调）；运行时由服务器实装插件承载 | WorkBuddy（agent）2026-10-05（task-212） |

> 完整文件清单见 `libs/` 各分类子目录（共 196 个文件）。新增/升级依赖仍须登记本表并走共享资源变更流程（`docs/git-workflow-policy.md` §4）。

> **关于 `libs/roslyn/10.0.401/` 的历史本机副本**：
> 合计 **27 MiB**，其中 `Microsoft.CodeAnalysis.CSharp.dll` 为 **18.3 MiB**，触发门禁 `NF-05`（P0：libs 新文件 > 10MiB 须改为入库的离线 `.nupkg`）。
> 同时原 `libs/roslyn/current.txt` 不符合 `libs/<分类>/<版本>/` 布局，会触发 `NF-03`（P0），故已删除。
> 该本机副本**不再是合规的直接程序集引用，也不得被项目通过 HintPath 引用**。若项目需要 Roslyn，
> 优先引用对应的 `Microsoft.CodeAnalysis*` NuGet 包并由 CPM 锁定版本，可使用全局缓存或配置源恢复；
> 只有包源无法恢复或明确要求离线构建时，才将匹配 `.nupkg` 提交到 `libs/nupkgs/` 并完成登记。

> 与 `vendor/` 的区别：`libs/` 放**二进制依赖**，`vendor/` 放**依赖源码**。
> 外部只读目录（历史遗留的 `Site19/Libraries`、服务端 `Managed` 等）**不再作为依赖位置**；如需引用其中程序集，必须先登记并复制到本目录对应分类下，再在项目中引用，禁止硬编码外部绝对路径。
