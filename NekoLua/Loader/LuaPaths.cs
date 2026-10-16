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
    static LuaPaths()
    {
        Root = PathManager.SecretLab.CreateSubdirectory("NekoLua");
        ModsRoot = Root.CreateSubdirectory("mods");
        LibsRoot = Root.CreateSubdirectory("dependencies");
        ConfigsRoot = Root.CreateSubdirectory("configs");

        var port = Server.Port.ToString();
        Mods = ModsRoot.CreateSubdirectory(port);
        Libs = LibsRoot.CreateSubdirectory(port);
        Configs = ConfigsRoot.CreateSubdirectory(port);

        GlobalMods = ModsRoot.CreateSubdirectory("global");
        GlobalLibs = LibsRoot.CreateSubdirectory("global");
    }
}
