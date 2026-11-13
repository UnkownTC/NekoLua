namespace NekoLua.Loader;

/// <summary>
/// 扩展生命周期回调：由 <see cref="LuaExtensionLoader"/> 在脚本加载前调用 <see cref="OnLoad"/>，
/// 插件禁用时按逆序调用 <see cref="OnUnload"/>。
/// </summary>
/// <remarks>
/// 扩展程序集无法卸载（net481 无 AssemblyLoadContext），所以 <see cref="OnLoad"/> 必须幂等、
/// <see cref="OnUnload"/> 必须把静态状态清干净，否则重载后会残留上一轮注册。
/// </remarks>
public interface ILuaExtension
{
    /// <summary>扩展标识（用于日志与重名检测）。</summary>
    string Id { get; }

    /// <summary>装载完成时调用（主线程，LuaLoader.LoadAll 之前）。</summary>
    void OnLoad();

    /// <summary>插件禁用时调用（主线程，LuaGameObject 销毁之前）。</summary>
    void OnUnload();
}
