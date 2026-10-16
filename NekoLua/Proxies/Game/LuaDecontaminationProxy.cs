using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using LabDecontam = LabApi.Features.Wrappers.Decontamination;

namespace NekoLua.Proxies.Game;

/// <summary>
/// decontamination 表：
///
///   属性（反射自 Decontamination）：
///     decontamination.status          string    "none" / "disabled" / "forced"（读写）
///     decontamination.isDecontaminating bool    只读
///     decontamination.serverTime      number    只读
///     decontamination.roundStartTime  number    只读
///     decontamination.offset          number    读写
///     decontamination.elevatorsText   string    读写
/// </summary>
public class LuaDecontaminationProxy : IRegisterLua
{
    public string Name { get; } = "decontamination";
    public Table Create(LuaModInstance mod)
    {
        return new LuaTypeMapper(mod.Script)
            .Scan(typeof(LabDecontam))
            .Build();
    }
}
