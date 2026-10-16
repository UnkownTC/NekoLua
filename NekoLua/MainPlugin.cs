using System.Reflection;
using LabApi.Features;
using LabApi.Loader.Features.Plugins;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Proxies;
using NekoLua.Registers;

namespace NekoLua;

public class MainPlugin : Plugin<Config>
{
    public static Assembly PluginAssembly { get; private set; }
    public static MainPlugin Instance { get; private set; } = null;
    public override string Name { get; } = "NekoLua";
    public override string Description { get; } = "A simple scpsl plugin, apply lua support";
    public override string Author { get; } = "UnkownTC";
    public override Version RequiredApiVersion => LabApiProperties.CurrentVersion;
    public override void Enable()
    {
        LoadConfigs();

        Instance = this;
        PluginAssembly = Assembly.GetExecutingAssembly();

        LuaEmbeddedExtractor.ExtractAll();

        LuaRegister.Clear();
        LuaRegister.Register(PluginAssembly);
        LuaGameObject.Create();

        LuaLoader.LoadAll();
    }
    public override void LoadConfigs()
    {
        base.LoadConfigs();

        if (Config != null)
            CacheConfig = Config;
    }
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
        LuaLoader.ClearAll();

        LuaGameObject.Destroy();

        Instance = null!;
        PluginAssembly = null!;
    }
    public static Config CacheConfig { get; private set; } = new();
}
