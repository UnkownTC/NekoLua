using LabApi.Features.Wrappers;
using LabApi.Loader.Features.Paths;

namespace NekoLua;

/// <summary>
/// 提供应用用于 Lua 模组、依赖项和配置的预定义目录路径。
/// </summary>
public static class LuaPaths
{
    /// <summary>
    /// 表示 NekoLua 的根目录，目录结构为：SCP Secret Laboratory\NekoLua\ 。
    /// </summary>
    public static DirectoryInfo Root { get; }
    /// <summary>
    /// 表示 Lua Mod 的根目录，目录结构为：SCP Secret Laboratory\NekoLua\mods\ 。
    /// </summary>
    public static DirectoryInfo ModsRoot { get; }
    /// <summary>
    /// 表示 Lua Mod 依赖 的根目录，目录结构为：SCP Secret Laboratory\NekoLua\dependencies\ 。
    /// </summary>
    public static DirectoryInfo DependenciesRoot { get; }
    /// <summary>
    /// 表示 Lua Mod 配置文件 的根目录，目录结构为：SCP Secret Laboratory\NekoLua\configs\ 。
    /// </summary>
    public static DirectoryInfo ConfigsRoot { get; }

    /// <summary>
    /// 表示 Lua Mod 的本服务器范围目录，目录结构为：SCP Secret Laboratory\NekoLua\mods\{服务器端口}\ 。
    /// </summary>
    public static DirectoryInfo Mods { get; }
    /// <summary>
    /// 表示 Lua Mod 依赖 的本服务器范围目录，目录结构为：SCP Secret Laboratory\NekoLua\dependencies\{服务器端口}\ 。
    /// </summary>
    public static DirectoryInfo Dependencies { get; }
    /// <summary>
    /// 表示 Lua Mod 配置文件 的本服务器范围目录，目录结构为：SCP Secret Laboratory\NekoLua\configs\{服务器端口}\ 。
    /// </summary>
    public static DirectoryInfo Configs { get; }

    /// <summary>
    /// 表示全局 Lua Mod 目录，目录结构为：SCP Secret Laboratory\NekoLua\mods\global\ 。
    /// </summary>
    public static DirectoryInfo GlobalMods { get; }
    /// <summary>
    /// 表示全局 Lua Mod 依赖 目录 的本服务器范围目录，目录结构为：SCP Secret Laboratory\NekoLua\dependencies\global\ 。
    /// </summary>
    public static DirectoryInfo GlobalDependencies { get; }
    static LuaPaths()
    {
        Root = PathManager.SecretLab.CreateSubdirectory("NekoLua");
        ModsRoot = Root.CreateSubdirectory("mods");
        DependenciesRoot = Root.CreateSubdirectory("dependencies");
        ConfigsRoot = Root.CreateSubdirectory("configs");

        var port = Server.Port.ToString();
        Mods = ModsRoot.CreateSubdirectory(port);
        Dependencies = DependenciesRoot.CreateSubdirectory(port);
        Configs = ConfigsRoot.CreateSubdirectory(port);

        GlobalMods = ModsRoot.CreateSubdirectory("global");
        GlobalDependencies = DependenciesRoot.CreateSubdirectory("global");
    }
}
