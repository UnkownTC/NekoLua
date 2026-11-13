using NekoLua.Loader;

namespace NekoLua.AstraBridge;

/// <summary>
/// 扩展生命周期：装载桥接（版本校验 / 事件类 / spawner），禁用时回收全部 Lua 内容。
/// </summary>
public sealed class AstraExtension : ILuaExtension
{
    public string Id => "NekoLua.Astra";

    public void OnLoad() => AstraBridge.Load();

    public void OnUnload() => AstraBridge.Unload();
}
