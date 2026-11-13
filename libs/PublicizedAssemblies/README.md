# libs/PublicizedAssemblies/ — 公开化依赖

存放经 `AssemblyPublicizer` 公开化后的只读参考程序集，供补丁项目调用其非公开成员（替代反射）。

## 目录结构

当前为**扁平命名**（每个公开化程序集一对文件，无子文件夹）：

```text
libs/PublicizedAssemblies/
├─ Assembly-CSharp-Publicized.dll              # 公开化 DLL（仅编译期可见性，运行时行为不变）
├─ Assembly-CSharp-firstpass-Publicized.dll
├─ CommandSystem.Core-Publicized.dll
├─ HintServiceMeow-Publicized.dll              # 来源：vendor/HintServiceMeow（DevMeow 分支，6.0.0.0）
├─ LabApi-Publicized.dll
├─ Mirror-Publicized.dll
└─ README.md                                  # 来源版本 / Publicizer 版本 / 生成命令
```

| 公开化 DLL | 来源（版本） | 生成方式 | 使用方 |
|---|---|---|---|
| `Assembly-CSharp-Publicized.dll` | `libs/scpsl-server/current/`（随上游） | Krafs.Publicizer 模式 B | Site19.PluginContent、LabApi.Helpers 等 30+ 引用 |
| `Assembly-CSharp-firstpass-Publicized.dll` | 同上 | 同上 | 同上 |
| `CommandSystem.Core-Publicized.dll` | 同上 | 同上 | 命令类插件 |
| `HintServiceMeow-Publicized.dll` | `vendor/HintServiceMeow`（`AssemblyVersion` 6.0.0.0，与 `libs/hintservicemeow/6.0.0.0/` 同版本） | Krafs.Publicizer 模式 B（公开化 `HintParser` / `ICoordinateTools` 等内部成员） | `src/Site19/AzureFramework`、`src/Site19/TextChatMeow/TextChatMeow`（2026-10-01 task-212 引入） |
| `LabApi-Publicized.dll` | `libs/labapi/1.1.7/` | 同上 | 多插件 |
| `Mirror-Publicized.dll` | `libs/scpsl-server/current/` | 同上 | 多插件 |

> ⚠️ `HintServiceMeow-Publicized.dll` 与 `Site19.PluginContent` 仍在用的 `vendor/HintServiceMeow` **源码 `ProjectReference`** 是同一程序集标识（`HintServiceMeow`）的两种来源：插件部署目录必须**只放公开化产物**，否则 `AzureFramework` 调用内部成员会在运行时抛 `MethodAccessException`。统一为单一来源（建议全部改引公开化 DLL）见 `docs/projects/Site19/README.md`「已知限制」。

> 原参考 DLL 仍留在 `libs/`（只读），本目录只放公开化产物；命名约定为 `<原名>-Publicized.dll`。如需多版本并存再启用 `<程序集名>/` 子文件夹分层。

## 生成（示例：Assembly-CSharp）

```bash
# 模式 A（推荐）：补丁项目 .csproj 写 <Publicize Include="Assembly-CSharp" />，构建期就地公开化，无需落盘
# 模式 B（跨多项目共享）：用 Publicizer 一次性生成 .pub.dll 落入本目录，再供各项目引用
```

## 引用（补丁项目，模式 B）

```xml
<Reference Include="Assembly-CSharp">
  <HintPath>$(MSBuildThisFileDirectory)..\..\libs\PublicizedAssemblies\Assembly-CSharp-Publicized.dll</HintPath>
  <Private>false</Private>
</Reference>
```

## 约束

- 只读、入库；变更 = 共享资源变更，交集成者。
- 上游参考 DLL 升级时须重新公开化并标注版本（避免与原版行为分叉）。
- 详见 `docs/dotnet-coding-standard.md` §9.8.4 与 `docs/tools/assemblypublicizer.md`。
