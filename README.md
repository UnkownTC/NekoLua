# NekoLua

SCP: Secret Laboratory 服务端 Lua 脚本插件（LabAPI），基于 MoonSharp 提供 Lua 运行时、
代理层与 RA 指令。

## 结构

```
src/NekoLua/          插件源码（127 个文件）
libs/                 只读依赖（游戏与 LabAPI 程序集）
tools/vsbuild.py      构建入口（net481 必需）
```

## 构建

```bash
dotnet restore src/NekoLua/NekoLua.csproj
python tools/vsbuild.py src/NekoLua/NekoLua.csproj -p:Configuration=Release -p:Platform=x64
```

产物：`artifacts/build/NekoLua/x64/Release/NekoLua.dll`

## 依赖

| 类型 | 来源 |
|---|---|
| MoonSharp / Newtonsoft.Json / Northwood.LabAPI / NVorbis / System.Net.Http | NuGet（CPM 锁定） |
| Assembly-CSharp(-firstpass) / CommandSystem.Core / Mirror / NorthwoodLib / Pooling | `libs/scpsl-server/current/` |
| LabApi | `libs/PublicizedAssemblies/LabApi-Publicized.dll` |
| UnityEngine.* / Unity.TextMeshPro | `libs/unity/current/` |

无对其他仓库工程（Site19.*、MengXinSheQu.*、Astra/AzureFramework）的 ProjectReference 或命名空间引用。
