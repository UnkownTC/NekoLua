# NekoLua 扩展契约

> 状态：v1（随 `0a00d81` 起可用）
> 适用：想在不修改 NekoLua 核心的前提下，给 Lua 增加新能力的插件作者。

## 1. 这是什么

NekoLua 核心在启动时扫描 `SecretLab/NekoLua/extensions/*.dll`，把其中实现了契约的类型装配进来：

| 契约 | 位置 | 作用 |
|---|---|---|
| `IRegisterLua` | `NekoLua.Registers` | 往每个 mod 的 `script.Globals` 注入一张 Lua 全局表 |
| `ILuaExtension` | `NekoLua.Loader` | 生命周期回调：`OnLoad` / `OnUnload` |
| `LuaCleanupRegistry` | `NekoLua.Dependencies` | 按 mod 登记清理动作，随 `reload` / `refresh` / `Disable` 回收 |

扩展程序集是普通 .NET Framework 4.8.1 类库，`ProjectReference` 指向 `NekoLua.dll`（或用编译产物），部署时把 DLL 放进 `extensions/` 即可，不需要重启之外的操作。

## 2. 最小示例

```csharp
using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;

namespace MyExtension;

public sealed class HelloRegister : IRegisterLua
{
    public string Name => "hello";           // Lua 侧全局名，必须全局唯一

    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);
        t["world"] = DynValue.NewCallback((_, _) => DynValue.NewString("world"));
        return t;
    }
}

public sealed class HelloExtension : ILuaExtension
{
    public string Id => "MyExtension";
    public void OnLoad() { /* 注册事件类 / 初始化静态状态 */ }
    public void OnUnload() { /* 清静态状态，扩展程序集本身无法卸载 */ }
}
```

多个 `IRegisterLua` 共用一个 `Name` 时，后注册者会被拒绝并记 Error（不会静默覆盖）。

## 3. 生命周期与时序

```
NekoLua.Enable()
  ├ LuaRegister.Register(NekoLua.dll)       内置表
  ├ LuaExtensionLoader.LoadAll()            扫描 extensions/*.dll
  │   ├ new IRegisterLua()                  每张表一个实例
  │   └ ILuaExtension.OnLoad()              初始化
  ├ LuaGameObject.Create()
  └ LuaLoader.LoadAll()                     每个 mod：Create(mod) → 执行 main.lua

NekoLua.Disable()
  ├ LuaLoader.ClearAll()
  ├ LuaExtensionLoader.UnloadAll()          OnUnload（逆序）
  └ LuaCleanupRegistry.ClearAll()
```

- `OnLoad` 在 mod 脚本执行**之前**调用，适合注册事件类、安装全局钩子。
- `Create(mod)` 每个 mod 调用一次；需要按 mod 隔离的状态放在闭包里，不要放静态字段。
- `OnUnload` 之后 `LuaRegister.Clear()` 已经清掉表实例，重载会重新实例化。

## 4. 按 mod 回收

扩展在 `Create(mod)` 里登记：

```csharp
LuaCleanupRegistry.Register(mod, owner =>
{
    // 释放该 mod 持有的资源；会被 reload / refresh 删除 / Disable 反复触发，必须幂等
});
```

- 动作**保留**在注册表中（reload 后仍会再次触发），所以不要在里面做「一次性注销」假设。
- 模组被彻底删除、资源已回收时调用 `LuaCleanupRegistry.Forget(mod)` 丢弃动作，避免持有 `Script` 引用。
- 需要区分「reload」与「删除」时，用「延迟一帧确认」模式：清理时先标记，`LuaMainThread.Queue` 到下一帧再检查是否被重新定义。

## 5. 线程与异常

| 规则 | 说明 |
|---|---|
| 主线程 | 触碰 Unity / 游戏对象必须在主线程；扩展自行记录 `Environment.CurrentManagedThreadId` 并校验 |
| 异常不跨界 | 回调里 `try/catch`，记录日志；不要把 .NET 异常抛进 Lua 虚拟机或游戏事件链 |
| 对象引用 | 用 `LuaObjectCache` 包装游戏对象，避免 Lua GC 与 Unity 生命周期冲突 |

核心已有的工具：`LuaMainThread.Queue`（主线程队列）、`LuaObjectCache`、`LuaTypeMapper`、`LuaArgs`、`LuaGlobalApiGuard`（门禁标签）均为 public。

## 6. 事件类扩展

内置 `eventhook` 只识别 LabAPI 的 `LabApi.Events.Handlers.*Events`。扩展可以追加自己的静态事件类：

```csharp
LuaEventHook.RegisterEventClass("mypack", typeof(MyPack.Events.PlayerEvents));
```

Lua 侧：

```lua
eventhook.bind("mypack", "Shooting", "id", function(ev) end)
```

## 7. 版本与兼容

- 扩展编译期绑定 NekoLua 的公开 API；核心删改公开签名属破坏性变更（AGENTS §6 按 MAJOR 处理）。
- net481 没有 `AssemblyLoadContext`，DLL 一旦加载**无法卸载**：扩展更新需要重启服务端；`OnUnload` 必须把静态状态清干净，`OnLoad` 必须幂等。
- 扩展程序集**不会被文件锁定**（NekoLua 用 `Assembly.Load(byte[])` 装载，而非 `LoadFrom`）：服务器运行期间可以直接覆盖 `NekoLua/extensions/*.dll`，替换后重启（或重新启用 NekoLua 插件）生效；但已加载的旧代码在进程退出前仍存活，不能指望热替换。
- 因为用字节装载，扩展**不能依赖 `extensions/` 目录里的其它 DLL**（没有目录探测）；依赖应放在 `plugins/` 由 LabAPI 加载，或合并进同一个扩展 DLL。
- 扩展程序集不会进入 Astra 之类的自动注册扫描（它不是 LabAPI 插件）；但放在 `plugins/` 下会被 LabAPI 当成插件加载，部署位置请固定在 `NekoLua/extensions/`。
