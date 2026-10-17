using LabApi.Features;

using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Game;

/// <summary>
/// labapi 表：
///   labapi.version                   -- 当前 LabApi 版本字符串，如 "1.0.0.0"
///   labapi.versionCompiled           -- 编译时版本字符串
/// </summary>
internal class LuaLabApiProxy : IRegisterLua
{
    public string Name { get; } = "labapi";
    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;
        var t = new Table(script)
        {
            ["versionCompiled"] = LabApiProperties.CompiledVersion,
            ["version"] = LabApiProperties.CurrentVersion.ToString(),
        };

        return t;
    }
}
