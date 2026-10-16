using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using PlayerRoles;

using PlayerStatsSystem;

namespace NekoLua.Proxies.Game;

/// <summary>
/// ragdoll 表：
///
///   静态查找：
///     ragdoll.list()                    -- 所有尸体
///     ragdoll.count()                   -- 数量
///     ragdoll.getByNickname(name)       -- 按昵称筛选
///     ragdoll.getByRole(roleName)       -- 按角色筛选
///     ragdoll.byPlayer(player)          -- 该玩家的尸体
///
///   创建：
///     ragdoll.spawnFromPlayer(player)              -- 从玩家位置生成
///     ragdoll.spawn(role, position, rotation, nickname)  -- 手动生成
///
///   批量：
///     ragdoll.destroyAll()              -- 销毁全部
///
///   实例属性（走通用代理）：
///     r.nickname（读写）/ r.role（读写）/ r.serial / r.position（读写）/
///     r.rotation（读写）/ r.scale（读写）/ r.isConsumed（读写）/
///     r.isDestroyed / r.damageHandler / r.base
///
///   实例方法：
///     r:isRevivableBy(player)           -- 指定 SCP-049 能否复活
///     r:destroy()                       -- 销毁
///     r:freeze() / r:unFreeze()
/// </summary>
public class LuaRagdollProxy : IRegisterLua
{
    public string Name { get; } = "ragdoll";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);
        var cache = new LuaObjectCache(s);


        t["list"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;
            foreach (var r in Ragdoll.List)
                arr[i++] = cache.Wrap(r);
            return DynValue.NewTable(arr);
        });

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(Ragdoll.List.Count));

        t["getByNickname"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("ragdoll.getByNickname(name)");

            var name = args[0].String;
            var arr = new Table(s);
            var i = 1;

            foreach (var r in Ragdoll.List)
            {
                try
                {
                    if (string.Equals(r.Nickname, name, StringComparison.OrdinalIgnoreCase))
                        arr[i++] = cache.Wrap(r);
                }
                catch { }
            }
            return DynValue.NewTable(arr);
        });

        t["getByRole"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("ragdoll.getByRole(roleName)");

            if (!Enum.TryParse<RoleTypeId>(args[0].String, true, out var role))
                throw new ScriptRuntimeException(
                    $"ragdoll.getByRole: 未知角色 '{args[0].String}'");

            var arr = new Table(s);
            var i = 1;

            foreach (var r in Ragdoll.List)
            {
                try
                { if (r.Role == role) arr[i++] = cache.Wrap(r); }
                catch { }
            }
            return DynValue.NewTable(arr);
        });

        t["byPlayer"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Table)
                throw new ScriptRuntimeException("ragdoll.byPlayer(player)");

            var userIdV = args[0].Table.Get("userId");
            if (userIdV.Type != DataType.String)
                throw new ScriptRuntimeException("ragdoll.byPlayer: player 缺少 userId");

            var arr = new Table(s);
            var i = 1;

            foreach (var r in Ragdoll.List)
            {
                try
                {
                    if (string.Equals(r.Nickname, userIdV.String, StringComparison.OrdinalIgnoreCase))
                        arr[i++] = cache.Wrap(r);
                }
                catch { }
            }
            return DynValue.NewTable(arr);
        });


        t["spawnFromPlayer"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Table)
                throw new ScriptRuntimeException("ragdoll.spawnFromPlayer(player)");

            var player = ResolvePlayer(args[0]) ?? throw new ScriptRuntimeException("ragdoll.spawnFromPlayer: 找不到玩家");
            try
            {
                var handler = new UniversalDamageHandler(0f, DeathTranslations.Unknown);
                var r = Ragdoll.SpawnRagdoll(player, handler);
                return r is null ? DynValue.Nil : cache.Wrap(r);
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"ragdoll.spawnFromPlayer: {ex.Message}");
            }
        });

        t["spawn"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 4 || args[0].Type != DataType.String || args[3].Type != DataType.String)
                throw new ScriptRuntimeException(
                    "ragdoll.spawn(role, position, rotation, nickname)");

            if (!Enum.TryParse<RoleTypeId>(args[0].String, true, out var role))
                throw new ScriptRuntimeException(
                    $"ragdoll.spawn: 未知角色 '{args[0].String}'");

            var position = LuaVector.UnpackV3(args, 1, "ragdoll.spawn");
            var rotation = LuaVector.UnpackQ(args, 2, "ragdoll.spawn");
            var nickname = args[3].String;

            try
            {
                var handler = new UniversalDamageHandler(0f, DeathTranslations.Unknown);
                var r = Ragdoll.SpawnRagdoll(role, position, rotation, handler, nickname);
                return r is null ? DynValue.Nil : cache.Wrap(r);
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"ragdoll.spawn: {ex.Message}");
            }
        });


        t["destroyAll"] = DynValue.NewCallback((_, _) =>
        {
            var n = 0;
            foreach (var r in Ragdoll.List)
            {
                try
                { r.Destroy(); n++; }
                catch { }
            }
            return DynValue.NewNumber(n);
        });

        return t;
    }
    private static Player? ResolvePlayer(DynValue v)
    {
        var userIdV = v.Table.Get("userId");
        return userIdV.Type != DataType.String ? null : Player.Get(userIdV.String);
    }
}
