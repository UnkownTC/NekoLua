using System.Reflection;
using LabApi.Features;
using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Plugins;
using NekoLua.Api;
using NekoLua.Loader;
using NekoLua.Proxies;
using NekoLua.Registers;

namespace NekoLua;

/// <summary>
/// NekoLua 插件入口类。
/// </summary>
public class MainPlugin : Plugin<Config>
{
    internal static Assembly PluginAssembly { get; private set; }
    /// <summary>
    /// 获取全局 MainPlugin 单例。
    /// </summary>
    public static MainPlugin Singleton { get; private set; } = null;
    /// <inheritdoc/>
    public override string Name { get; } = "NekoLua";
    /// <inheritdoc/>
    public override string Description { get; } = "A simple scpsl plugin, apply lua support.";
    /// <inheritdoc/>
    public override string Author { get; } = "UnkownTC & Supper-Steve & xuhan321";
    /// <inheritdoc/>
    public override Version Version { get; } = new(1, 0, 1);
    /// <inheritdoc/>
    public override Version RequiredApiVersion => LabApiProperties.CurrentVersion;
    /// <inheritdoc/>
    public override void Enable()
    {
        LoadConfigs();

        Singleton = this;
        PluginAssembly = Assembly.GetExecutingAssembly();

        LuaEmbeddedExtractor.ExtractAll();

        LuaRegister.Clear();
        LuaRegister.Register(PluginAssembly);
        LuaGameObject.Create();

        LuaModLoader.LoadAll();
    }
    /// <inheritdoc/>
    public override void LoadConfigs()
    {
        base.LoadConfigs();

        if (Config != null)
            CacheConfig = Config;
    }
    /// <inheritdoc/>
    public override void Disable()
    {
        LuaRegister.Clear();
        LuaTimer.ClearAll();
        LuaEventHook.ClearAll();
        LuaCommandHook.ClearAll();
        LuaEventBus.ClearAll();
        LuaNetworkManager.ClearAll();
        LuaScheduler.ClearAll();
        LuaAsyncScheduler.ClearAll();
        LuaRegexProxy.ClearAll();
        LuaMainThread.Clear();
        LuaModLoader.ClearAll();

        LuaGameObject.Destroy();

        Singleton = null!;
        PluginAssembly = null!;
    }
    /// <summary>
    /// 用于存储全局缓存配置的 Config 实例。
    /// </summary>
    /// <remarks>若无法读取 Config 实例，自动使用默认值。</remarks>
    public static Config CacheConfig { get; private set; } = new();
}
