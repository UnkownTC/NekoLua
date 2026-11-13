using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Paths;

namespace NekoLua.Loader;

public static class LuaPaths
{
    public static DirectoryInfo Root { get; }

    public static DirectoryInfo ModsRoot { get; }
    public static DirectoryInfo LibsRoot { get; }
    public static DirectoryInfo ConfigsRoot { get; }

    public static DirectoryInfo Mods { get; }
    public static DirectoryInfo Libs { get; }
    public static DirectoryInfo Configs { get; }

    public static DirectoryInfo GlobalMods { get; }
    public static DirectoryInfo GlobalLibs { get; }

    /// <summary>
    /// 扩展程序集目录（<c>NekoLua/extensions</c>）：放置引用 NekoLua 的桥接 DLL，
    /// 由 <see cref="LuaExtensionLoader"/> 在脚本加载前扫描；核心不引用其中任何程序集。
    /// </summary>
    public static DirectoryInfo Extensions { get; }
    static LuaPaths()
    {
        Root = PathManager.SecretLab.CreateSubdirectory("NekoLua");
        ModsRoot = Root.CreateSubdirectory("mods");
        LibsRoot = Root.CreateSubdirectory("dependencies");
        ConfigsRoot = Root.CreateSubdirectory("configs");
        Extensions = Root.CreateSubdirectory("extensions");

        var port = Server.Port.ToString();
        Mods = ModsRoot.CreateSubdirectory(port);
        Libs = LibsRoot.CreateSubdirectory(port);
        Configs = ConfigsRoot.CreateSubdirectory(port);

        GlobalMods = ModsRoot.CreateSubdirectory("global");
        GlobalLibs = LibsRoot.CreateSubdirectory("global");
    }
}
