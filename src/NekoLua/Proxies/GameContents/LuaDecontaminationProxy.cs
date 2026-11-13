using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using LabDecontam = LabApi.Features.Wrappers.Decontamination;

namespace NekoLua.Proxies.GameContents;

/// <summary>
/// decontamination 表：
///
///   属性（反射自 Decontamination）：
///     decontamination.status          string    "none" / "disabled" / "forced"（写受门控）
///     decontamination.isDecontaminating bool    只读
///     decontamination.serverTime      number    只读
///     decontamination.roundStartTime  number    只读
///     decontamination.offset          number    写受门控
///     decontamination.elevatorsText   string    写受门控
///
/// 写操作受 <c>Config.DeniedGlobalApis</c> 的 <c>decontamination</c> 标签门控。
/// </summary>
public class LuaDecontaminationProxy : IRegisterLua
{
    public string Name { get; } = "decontamination";
    public Table Create(LuaModInstance mod)
    {
        return new LuaTypeMapper(mod.Script)
            .Prefix(Name)
            .Scan(typeof(LabDecontam))
            .GuardWrites("decontamination", "status", "offset", "elevatorsText")
            .Build();
    }
}
