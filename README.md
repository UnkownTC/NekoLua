# NekoLua

SCP: Secret Laboratory 服务端 Lua 脚本插件（LabAPI），基于 MoonSharp 提供 Lua 运行时、
代理层与 RA 指令。

## 结构

```
src/NekoLua/          插件源码（核心，不引用任何其它插件工程）
src/NekoLua.Astra/    可选桥接工程：把 AstraFramework 的 CustomRole/Team/Wave 暴露给 Lua
libs/                 只读依赖（游戏、LabAPI 与可选桥接的 Astra 程序集）
tools/vsbuild.py      构建入口（net481 必需）
```

## 构建

```bash
# 核心（可单独构建发布）
dotnet restore src/NekoLua/NekoLua.csproj
python tools/vsbuild.py src/NekoLua/NekoLua.csproj -p:Configuration=Release -p:Platform=x64

# 可选桥接（需要 AstraFramework >= 3.2.0，DLL 已放在 libs/astra/3.2.0/）
dotnet restore src/NekoLua.Astra/NekoLua.Astra.csproj
python tools/vsbuild.py src/NekoLua.Astra/NekoLua.Astra.csproj -p:Configuration=Release -p:Platform=x64
```

产物：

- `artifacts/build/NekoLua/x64/Release/net481/NekoLua.dll`
- `artifacts/build/NekoLua.Astra/x64/Release/net481/NekoLua.Astra.dll`（部署到 `SecretLab/NekoLua/extensions/`）

## 依赖

| 类型 | 来源 |
|---|---|
| MoonSharp / Newtonsoft.Json / Northwood.LabAPI / NVorbis / System.Net.Http | NuGet（CPM 锁定） |
| Assembly-CSharp(-firstpass) / CommandSystem.Core / Mirror / NorthwoodLib / Pooling | `libs/scpsl-server/current/` |
| LabApi | `libs/PublicizedAssemblies/LabApi-Publicized.dll` |
| UnityEngine.* / Unity.TextMeshPro | `libs/unity/current/` |
| Astra.Framework（仅 `NekoLua.Astra` 工程） | `libs/astra/3.2.0/Astra.Framework.dll` |

**核心工程不引用任何其它插件工程**：Astra 只被可选的 `NekoLua.Astra` 引用，且该程序集由
`NekoLua/extensions/` 目录在运行时扫描装载，核心程序集不需要 Astra 即可编译发布。

## 扩展

第三方能力通过 `NekoLua/extensions/*.dll` 接入，不修改核心：

| 契约 | 用途 |
|---|---|
| `NekoLua.Registers.IRegisterLua` | 新增 Lua 全局表（`Name` 全局唯一，重名拒绝） |
| `NekoLua.Loader.ILuaExtension` | 生命周期回调（`OnLoad` / `OnUnload`） |
| `NekoLua.Dependencies.LuaCleanupRegistry` | 按 mod 登记清理动作，随 `reload`/`refresh`/`Disable` 回收 |

详见 [docs/EXTENSIONS.md](docs/EXTENSIONS.md)；Astra 桥接方案见
[docs/ASTRA-EXTENSION.md](docs/ASTRA-EXTENSION.md)。
