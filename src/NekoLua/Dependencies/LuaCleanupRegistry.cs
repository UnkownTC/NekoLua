using LabApi.Features.Console;

using NekoLua.Loader;

namespace NekoLua.Dependencies;

/// <summary>
/// 按 Lua 模组归属的清理注册表。
///
/// <para>核心的清理链（<see cref="LuaLoader"/> 的 CleanupMod 与 <see cref="NekoLua.MainPlugin"/> 的 Disable）
/// 原本逐行硬编码，扩展新增的「按 mod 持有资源」没有插槽，热重载必然泄漏。
/// 扩展在 <c>Create(mod)</c> 时通过本表登记清理动作，即可与内置子系统走同一条回收路径。</para>
///
/// <para>语义：<b>清理即所有权释放</b>。动作应可重复调用（reload 会反复触发），
/// 并且不得假设 Astra/游戏侧仍然存活。模组被彻底移除、资源已由其它路径回收时，
/// 用 <see cref="Forget"/> 丢弃动作（不执行），避免 Script 引用长期滞留。</para>
/// </summary>
public static class LuaCleanupRegistry
{
    private static readonly Dictionary<LuaModInstance, List<Action<LuaModInstance>>> _actions = [];

    /// <summary>
    /// 为指定模组登记一个清理动作。
    /// </summary>
    /// <param name="owner">归属模组实例。</param>
    /// <param name="cleanup">清理动作；异常会被捕获并记录，不影响其它动作。</param>
    public static void Register(LuaModInstance owner, Action<LuaModInstance> cleanup)
    {
        if (owner is null || cleanup is null)
            return;

        if (!_actions.TryGetValue(owner, out var list))
            _actions[owner] = list = [];

        list.Add(cleanup);
    }

    /// <summary>执行某个模组登记的全部清理动作（动作保留，供后续 reload 再次触发）。</summary>
    /// <param name="owner">归属模组实例。</param>
    internal static void Cleanup(LuaModInstance owner)
    {
        if (owner is null || !_actions.TryGetValue(owner, out var list))
            return;

        foreach (var action in list)
        {
            try
            {
                action(owner);
            }
            catch (Exception ex)
            {
                Logger.Error($"[{owner.Name}] 清理动作异常: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 丢弃某个模组的清理动作而不执行（模组已被彻底移除、资源已由其它路径回收时调用）。
    /// </summary>
    /// <param name="owner">归属模组实例。</param>
    public static void Forget(LuaModInstance owner)
    {
        if (owner is not null)
            _ = _actions.Remove(owner);
    }

    /// <summary>执行并清空全部清理动作（插件 Disable 时调用）。</summary>
    internal static void ClearAll()
    {
        foreach (var owner in _actions.Keys.ToArray())
            Cleanup(owner);

        _actions.Clear();
    }
}
