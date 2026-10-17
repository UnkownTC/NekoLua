# NekoLua

NekoLua 是一个现代化的 SCP:SL 插件框架

它基于 [LabApi](https://github.com/northwood-studios/LabAPI) 与 [MoonSharp](https://www.moonsharp.org/)，把 Lua 语言 带到了 SCP:SL 内，能让开发者使用 Lua 就能够编出代替复杂插件功能的 Mod 。

其上手难度简单，可以让广大服主**少花冤枉钱**在一些极其简单的插件之上。

## 优点：

- 目录隔离：仿照LabAPI，对mod目录进行隔离，防止Mod冲突。

- 热重载：修改脚本 → nekolua reload → 立即生效，节省重新编译dll插件与重启服务器的时间。

- 反射与缓存：通过反射代理，将C#的对象将会被暴露给Lua。

> NekoLua 依靠反射与缓存来记录对象，**理论上** NekoLua 支持所有 LabAPI 的事件。

## 适合人群：

- **服主** —— 用现成的 mod 定制服务器行为，不改一行 C#、不重编译插件，**少花冤枉钱**
- **脚本作者** —— 熟悉 Lua，而不熟悉 C# 也能写游戏插件
- **C# 开发者** —— 把复杂逻辑写成 Lua 代码，快速验证想法，稳定后再迁移到 C#
- **服务器玩家** —— 参与服务器建设，亦或者向大家展示自己实力

适合人群广泛，最简单的可乐不掉血仅需 6 行代码就可以完成：

```lua
eventhook.unbind("scp207NoDamage")
eventhook.bind("player", "Hurting", "scp207NoDamage", function(ev)
    if ev.damageHandler.translationId and ev.damageHandler.translationId == 10 then
        ev.isAllowed = false
    end
end)
```

## 后记：

NekoLua 是为 SCP: Secret Laboratory 打造的复杂 Lua 脚本引擎，你可以通过教程学习。

> 项目地址：<https://github.com/UnkownTC/NekoLua>

> 文档 Wiki：<https://github.com/UnkownTC/NekoLua/wiki>

---

> ⚠ NekoLua 的 mod 运行在同一进程内，拥有与插件同等的信任级别，记住一定要安装**你信任的 Mod**！
