using System.Reflection;
using LabApi.Features;
using LabApi.Features.Console;
using LabApi.Loader.Features.Plugins;
using NekoLua.Registers;
using NekoLua.SecretLabNAudioProxy.Reading;
using SecretLabNAudio.Core.FileReading;

namespace NekoLua.SecretLabNAudioProxy;

public class MainPlugin : Plugin
{
    private static Assembly _assembly;
    public static MainPlugin Instance { get; private set; } = null;
    public override string Name { get; } = "NekoLua.SecretLabNAudio";
    public override string Description { get; } = "A support plugin for SecretLabNAudio.";
    public override string Author { get; } = "UnkownTC";
    public override Version RequiredApiVersion => LabApiProperties.CurrentVersion;
    public override void Enable()
    {
        LoadConfigs();

        Instance = this;
        _assembly = Assembly.GetExecutingAssembly();
        LuaRegister.Register(_assembly);

        try
        {
            if (!AudioReaderFactoryManager.TryGetFactory(".ogg", out _))
                AudioReaderFactoryManager.RegisterFactory(".ogg", new OggAudioReaderFactory());
        }
        catch (TypeLoadException) { Logger.Warn("当前缺少依赖\"NAudio.Vorbis\"，可能会造成ogg音频无法被加载，如果不需要支持ogg音频可以忽略此条警告。"); }
    }
    public override void Disable()
    {
        Instance = null;
        _assembly = null;
    }
}
