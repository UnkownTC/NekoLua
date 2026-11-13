using LabApi.Features;
using LabApi.Features.Enums;
using LabApi.Features.Extensions;
using LabApi.Loader;
using LabApi.Loader.Constants;
using LabApi.Loader.Features.Misc;
using LabApi.Loader.Features.Paths;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.GameContents;

/// <summary>
/// labapi 表（LabAPI 宿主自身信息与只读工具面）：
///
///   标识：
///     labapi.version                   string   LabAPI 当前版本
///     labapi.versionCompiled           string   编译期 LabAPI 版本
///     labapi.isInitialized             bool     LabAPI（PluginLoader）是否已初始化
///
///   路径（只读字符串；读写仍受 LuaPathGuard 的 mod 作用域白名单约束）：
///     labapi.paths.appData / secretLab / labApi / plugins / dependencies / configs
///     labapi.paths.raConfigPath / gameplayConfigPath / sharingConfigPath /
///                  mutesConfigPath / userIdBansPath / ipBansPath /
///                  whitelistConfigPath / reservedSlotsConfigPath
///
///   插件与依赖（只读）：
///     labapi.plugins.count() / enabledCount() / list() / dependencies() / config()
///
///   命令（只读）：
///     labapi.commands.types() / count() / names()
///
///   程序集（只读）：
///     labapi.assemblies.loaded() / missing(name)
///
///   枚举与常量：
///     labapi.enums.commandType / playerSearchFlags / scp079PingType / doorName
///     labapi.priority.highest / high / medium / low / lowest
///
///   RoleTypeId 判定（role.*，对应 LabAPI <c>RoleExtensions</c>）：
///     labapi.roles.isScp/idDead/isNtf/isChaos/isMilitary/isCivilian(role)   -> bool
///     labapi.roles.fullName(role)                                          -> string
///
///   事件与表统计：
///     labapi.events.classCount() / eventCount() / boundCount() / byClass()
///     labapi.apis()                    -- 当前已注册的全部 Lua 表名
/// </summary>
public class LuaLabApiProxy : IRegisterLua
{
    public string Name { get; } = "labapi";

    public Table Create(LuaModInstance mod)
    {
        var script = mod.Script;

        var t = new Table(script)
        {
            ["versionCompiled"] = LabApiProperties.CompiledVersion,
            ["version"] = LabApiProperties.CurrentVersion.ToString(),
            ["isInitialized"] = PluginLoader.Initialized,

            // PathManager 的 public static 属性全部是 DirectoryInfo / string，
            // 两者都在 LuaTypeMapper.SimpleTypes 中，直接 Scan 即可。
            ["paths"] = new LuaTypeMapper(script).Scan(typeof(PathManager)).Build(),

            ["priority"] = new LuaTypeMapper(script)
                .ScanStaticFields(typeof(Priority))
                .Build(),

            ["roles"] = new LuaTypeMapper(script)
                .ScanStaticMethods(typeof(RoleExtensions))
                .Build(),
        };

        t["plugins"] = BuildPlugins(script);
        t["commands"] = BuildCommands(script);
        t["assemblies"] = BuildAssemblies(script);
        t["enums"] = BuildEnums(script);
        t["events"] = BuildEvents(script);

        t["apis"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(script);
            var i = 1;

            foreach (var name in LuaRegister.Registers.Select(r => r.Name).OrderBy(n => n))
                arr[i++] = DynValue.NewString(name);

            return DynValue.NewTable(arr);
        });

        return t;
    }

    /// <summary>
    /// LabAPI 插件清单。**只读**：不暴露 <c>LoadPlugins</c> / <c>EnablePlugin</c>，
    /// 插件生命周期归宿主，脚本无权改动。
    /// </summary>
    private static DynValue BuildPlugins(Script script)
    {
        var t = new Table(script);

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(PluginLoader.Plugins.Count));

        t["enabledCount"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(PluginLoader.EnabledPlugins.Count));

        t["list"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(script);
            var i = 1;

            foreach (var kv in PluginLoader.Plugins)
            {
                var plugin = kv.Key;

                arr[i++] = DynValue.NewTable(new Table(script)
                {
                    ["name"] = plugin.Name,
                    ["version"] = plugin.Version?.ToString() ?? "0.0.0",
                    ["author"] = plugin.Author,
                    ["description"] = plugin.Description,
                    ["priority"] = plugin.Priority.ToString().ToLowerInvariant(),
                    ["isEnabled"] = PluginLoader.EnabledPlugins.Contains(plugin),
                    ["isTransparent"] = plugin.IsTransparent,
                    ["filePath"] = plugin.FilePath,
                });
            }

            return DynValue.NewTable(arr);
        });

        t["dependencies"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(script);
            var i = 1;

            foreach (var asm in PluginLoader.Dependencies.OrderBy(a => a.FullName))
                arr[i++] = DynValue.NewString(asm.FullName ?? asm.GetName().Name ?? "");

            return DynValue.NewTable(arr);
        });

        t["config"] = DynValue.NewCallback((_, _) =>
        {
            var cfg = PluginLoader.Config;

            var tbl = new Table(script)
            {
                ["dependencyPaths"] = ToStringArray(script, cfg?.DependencyPaths),
                ["pluginPaths"] = ToStringArray(script, cfg?.PluginPaths),
                ["loadUnsupportedPlugins"] = cfg?.LoadUnsupportedPlugins ?? false,
            };

            return DynValue.NewTable(tbl);
        });

        return DynValue.NewTable(t);
    }

    /// <summary>
    /// LabAPI 命令加载器统计（只读）。注册/反注册走 NekoLua 自己的 <c>command</c> 表，
    /// 此处只暴露宿主侧的可观测信息。
    /// </summary>
    private static DynValue BuildCommands(Script script)
    {
        var t = new Table(script);

        t["types"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(script);
            var i = 1;

            foreach (var type in CommandLoader.CommandHandlers.Keys.Select(k => k.Name).OrderBy(n => n))
                arr[i++] = DynValue.NewString(type);

            return DynValue.NewTable(arr);
        });

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(CommandLoader.CommandHandlers.Sum(kv => kv.Value.Count)));

        t["names"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(script);
            var i = 1;

            foreach (var kv in CommandLoader.RegisteredCommands)
            {
                foreach (var cmd in kv.Value)
                    arr[i++] = DynValue.NewString(cmd.Command);
            }

            foreach (var cmd in CommandLoader.LabApiCommands ?? [])
                arr[i++] = DynValue.NewString(cmd.Command);

            return DynValue.NewTable(arr);
        });

        return DynValue.NewTable(t);
    }

    /// <summary>
    /// 程序集只读面。**刻意只开两个只读方法**：
    /// <c>LoadEmbeddedDll</c> / <c>LoadCompressedEmbeddedDll</c> / <c>ResolveEmbeddedResources</c>
    /// 会加载任意程序集（等价代码执行），与 MoonSharp <c>Preset_HardSandbox</c> 冲突，一律不暴露。
    /// </summary>
    private static DynValue BuildAssemblies(Script script)
    {
        var t = new Table(script);

        t["loaded"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(script);
            var i = 1;

            foreach (var name in AssemblyUtils.GetLoadedAssemblies().OrderBy(n => n))
                arr[i++] = DynValue.NewString(name);

            return DynValue.NewTable(arr);
        });

        t["missing"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("labapi.assemblies.missing(name): 需要字符串");

            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(
                    a.GetName().Name,
                    args[0].String,
                    StringComparison.OrdinalIgnoreCase));

            if (asm is null)
                return DynValue.Nil;

            var arr = new Table(script);
            var i = 1;

            foreach (var name in AssemblyUtils.GetMissingDependencies(asm).OrderBy(n => n))
                arr[i++] = DynValue.NewString(name);

            return DynValue.NewTable(arr);
        });

        return DynValue.NewTable(t);
    }

    /// <summary>
    /// LabAPI 四个公开枚举的取值表（名称 → 数值）。
    /// 供脚本在需要数值时（Flags 组合、<c>player.getAll(flags)</c> 等）取常量；
    /// 非 Flags 枚举在各 API 里仍可直接传小写枚举名字符串。
    /// </summary>
    private static DynValue BuildEnums(Script script)
    {
        var t = new Table(script);

        t["commandType"] = EnumTable(script, typeof(CommandType));
        t["playerSearchFlags"] = EnumTable(script, typeof(PlayerSearchFlags));
        t["scp079PingType"] = EnumTable(script, typeof(Scp079PingType));
        t["doorName"] = EnumTable(script, typeof(DoorName));

        return DynValue.NewTable(t);
    }

    private static DynValue EnumTable(Script script, Type enumType)
    {
        var tbl = new Table(script);

        foreach (var name in Enum.GetNames(enumType))
        {
            var key = char.ToLowerInvariant(name[0]) + name.Substring(1);

            tbl[key] = Convert.ToInt64(
                Enum.Parse(enumType, name),
                System.Globalization.CultureInfo.InvariantCulture);
        }

        return DynValue.NewTable(tbl);
    }

    /// <summary>
    /// 事件系统统计。事件本身的注册/反注册走 <c>eventhook</c> 表，
    /// 此处只提供「LabAPI 提供了多少事件 / 当前脚本挂了多少」的可观测口径。
    /// </summary>
    private static DynValue BuildEvents(Script script)
    {
        var t = new Table(script);

        t["classCount"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LuaEventHook.ClassCount));

        t["eventCount"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LuaEventHook.EventCount));

        t["boundCount"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LuaEventHook.BoundCount));

        t["byClass"] = DynValue.NewCallback((_, _) =>
        {
            var tbl = new Table(script);

            foreach (var kv in LuaEventHook.BoundCountByClass())
                tbl[kv.Key] = kv.Value;

            return DynValue.NewTable(tbl);
        });

        return DynValue.NewTable(t);
    }

    private static DynValue ToStringArray(Script script, IEnumerable<string>? source)
    {
        var arr = new Table(script);
        var i = 1;

        foreach (var s in source ?? [])
            arr[i++] = DynValue.NewString(s);

        return DynValue.NewTable(arr);
    }
}
