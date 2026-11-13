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
    public override string Description { get; } = "Steve's Lua Plugin";
    public override string Author { get; } = "史蒂夫";
    public override Version RequiredApiVersion => LabApiProperties.CurrentVersion;
    public override void Enable()
    {
        LoadConfigs();

        Instance = this;
        PluginAssembly = Assembly.GetExecutingAssembly();


        LuaEmbeddedExtractor.ExtractAll();

        LuaRegister.Clear();
        LuaRegister.Register(PluginAssembly);
        LuaExtensionLoader.LoadAll();
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
        LuaWin32Native.ClearAll();
        LuaMainThread.Clear();
        LuaLoader.ClearAll();
        LuaExtensionLoader.UnloadAll();
        LuaCleanupRegistry.ClearAll();

        LuaGameObject.Destroy();

        Instance = null!;
        PluginAssembly = null!;
    }
    public static Config CacheConfig { get; private set; } = new();
}
