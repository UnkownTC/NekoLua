using MoonSharp.Interpreter;

namespace NekoLua.Dependencies;

/// <summary>
/// 全服级危险 API 门禁层（审查 P2-14）。
///
/// 这些 API 一次调用即可改变整个服务器状态（核弹、回合重启、白名单、全服广播等）。
/// 作为脚本平台，默认**不**禁用（保持既有行为），由运营者按信任模型通过
/// <c>Config.DeniedGlobalApis</c> 填入标签来逐类关闭。
///
/// 可用标签（task-212 起）：<c>warhead</c> / <c>round</c> / <c>server</c> /
/// <c>whitelist</c> / <c>reservedSlots</c> / <c>permissions</c> / <c>broadcast</c> /
/// <c>cassie</c> / <c>decontamination</c> / <c>map</c> / <c>hazard</c> /
/// <c>pocket</c> / <c>scp914</c>。
/// 完整清单见 <c>docs/projects/NekoLua/API.md</c> §6.5。
///
/// <para><b>已知边界</b>：门禁挂在本层显式登记的入口上，不覆盖经由
/// <see cref="LuaObjectProxy"/> 反射得到的**实例成员**（例如 <c>door:destroy()</c>、
/// <c>locker:clearAllChambers()</c>）。实例级破坏性调用的隔离靠 LabAPI 自身的
/// 对象生命周期与运营者的脚本审计，不在本门禁职责内。</para>
/// </summary>
public static class LuaGlobalApiGuard
{
    public static void EnsureAllowed(string tag, string api)
    {
        var denied = MainPlugin.CacheConfig.DeniedGlobalApis;

        if (denied is not { Length: > 0 })
            return;

        foreach (var item in denied)
        {
            if (string.Equals(item, tag, StringComparison.OrdinalIgnoreCase))
                throw new ScriptRuntimeException(
                    $"{api}: 该全服级 API 已被服务器禁用（Config.DeniedGlobalApis 含 '{tag}'）");
        }
    }
}
