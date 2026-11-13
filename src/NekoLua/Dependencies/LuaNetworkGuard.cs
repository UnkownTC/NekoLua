using System.Net;
using System.Net.Sockets;

using MoonSharp.Interpreter;

namespace NekoLua.Dependencies;

/// <summary>
/// 网络目标门禁：阻断脚本访问本机 / 内网 / 链路本地 / 云元数据地址（SSRF 纵深防御，审查 P2-18）。
///
/// 由 <c>Config.BlockPrivateNetworkTargets</c> 控制，默认开启。
/// 覆盖范围：IP 字面量（v4/v6，含 IPv4-mapped）、<c>Uri.IsLoopback</c>、以及常见元数据主机名。
/// 已知残留：纯主机名（非上述名单）经 DNS 解析到内网地址（DNS rebinding）不在本轮覆盖范围内，
/// 见 <c>docs/plans/2026-10-02-task-212-nekolua-optimization.md</c> §3「暂不处理」。
/// </summary>
public static class LuaNetworkGuard
{
    private static readonly string[] BlockedHostNames =
    [
        "localhost",
        "metadata",
        "metadata.google.internal",
        "metadata.goog",
        "instance-data",
        "instance-data.ec2.internal",
    ];

    /// <summary>校验主机名（或 IP 字面量）是否允许访问。</summary>
    public static void EnsureHostAllowed(string? host, string prefix)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ScriptRuntimeException($"{prefix}: host 不能为空");

        if (!MainPlugin.CacheConfig.BlockPrivateNetworkTargets)
            return;

        var trimmed = host!.Trim().Trim('[', ']');

        if (IPAddress.TryParse(trimmed, out var ip))
        {
            if (IsBlockedAddress(ip))
                throw new ScriptRuntimeException(
                    $"{prefix}: 禁止访问本机/内网地址 '{host}'（Config.BlockPrivateNetworkTargets）");

            return;
        }

        foreach (var name in BlockedHostNames)
        {
            if (string.Equals(trimmed, name, StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("." + name, StringComparison.OrdinalIgnoreCase))
                throw new ScriptRuntimeException(
                    $"{prefix}: 禁止访问本机/内网地址 '{host}'（Config.BlockPrivateNetworkTargets）");
        }
    }

    /// <summary>校验 URI 是否允许访问（http/ws 等走 URI 的入口）。</summary>
    public static void EnsureUriAllowed(Uri uri, string prefix)
    {
        if (!MainPlugin.CacheConfig.BlockPrivateNetworkTargets)
            return;

        if (uri.IsLoopback)
            throw new ScriptRuntimeException(
                $"{prefix}: 禁止访问本机地址 '{uri.Host}'（Config.BlockPrivateNetworkTargets）");

        EnsureHostAllowed(uri.Host, prefix);
    }

    /// <summary>判断单个 IP 是否属于被阻断的地址段。</summary>
    public static bool IsBlockedAddress(IPAddress ip)
    {
        if (ip is null)
            return true;

        if (ip.IsIPv4MappedToIPv6)
            return IsBlockedAddress(ip.MapToIPv4());

        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();

            if (b[0] == 0 || b[0] == 10 || b[0] == 127)
                return true;

            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                return true;

            if (b[0] == 192 && b[1] == 168)
                return true;

            if (b[0] == 169 && b[1] == 254)
                return true;

            // 组播 / 保留（含 255.255.255.255）
            return b[0] >= 224;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6Loopback))
                return true;

            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
                return true;

            // fc00::/7 唯一本地地址
            var b = ip.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC;
        }

        return false;
    }
}
