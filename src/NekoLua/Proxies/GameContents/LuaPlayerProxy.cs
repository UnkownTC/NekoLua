using LabApi.Features.Enums;
using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.GameContents;

/// <summary>
/// player 表：
///   player.list() / readyList() / count() / readyCount()
///   player.npcList() / dummyList() / regularNpcList() / unauthenticatedList()
///   player.getAll([flags])          -- 按 PlayerSearchFlags 组合筛选（字符串或位值）
///   player.get(id)                  -- 按 networkId / userId / nickname 查
///   player.getByNickname(name[, requireFullMatch])
///   player.getByDisplayName(name[, requireFullMatch])
///   player.tryGetPlayersByName(name) -> table
///   player.random() / host()
///
/// 实例代理（走 LuaObjectProxy）：
///   nickname / health / maxHealth / position / rotation / role / team
///   isAlive / isSCP / isHuman / inventory / currentItem
///   及 Player 类的全部 public 成员
/// </summary>
public class LuaPlayerProxy : IRegisterLua
{
    public string Name { get; } = "player";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);
        var cache = new LuaObjectCache(s);

        t["list"] = LuaTableFactory.Collection(s, cache, () => Player.List);
        t["readyList"] = LuaTableFactory.Collection(s, cache, () => Player.ReadyList);

        t["npcList"] = LuaTableFactory.Collection(s, cache, () => Player.NpcList);
        t["dummyList"] = LuaTableFactory.Collection(s, cache, () => Player.DummyList);
        t["regularNpcList"] = LuaTableFactory.Collection(s, cache, () => Player.RegularNpcList);
        t["unauthenticatedList"] = LuaTableFactory.Collection(
            s,
            cache,
            () => Player.UnauthenticatedList);

        t["getAll"] = DynValue.NewCallback((_, args) =>
        {
            var flags = args.Count > 0
                ? LuaArgs.ParseFlags<PlayerSearchFlags>(args[0], "player.getAll(flags)")
                : PlayerSearchFlags.AuthenticatedAndDummy;

            return DynValue.NewTable(WrapAll(s, cache, Player.GetAll(flags)));
        });

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(Player.Count));

        t["readyCount"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(Player.ReadyList.Count()));

        t["host"] = DynValue.NewCallback((_, _) =>
            Server.Host is null ? DynValue.Nil : cache.Wrap(Server.Host));

        t["get"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("player.get(id): 需要字符串");

            var id = args[0].String;

            foreach (var p in Player.List)
            {
                if (MatchesId(p, id))
                    return cache.Wrap(p);
            }

            return DynValue.Nil;
        });

        t["getByNickname"] = DynValue.NewCallback((_, args) =>
        {
            var name = LuaArgs.CollectStrings(args, 0, 1, "player.getByNickname(name[, requireFullMatch])")[0];
            var full = args.Count > 1 && args[1].Type == DataType.Boolean && args[1].Boolean;

            var found = Player.GetByNickname(name, full);
            return found is null ? DynValue.Nil : cache.Wrap(found);
        });

        t["getByDisplayName"] = DynValue.NewCallback((_, args) =>
        {
            var name = LuaArgs.CollectStrings(args, 0, 1, "player.getByDisplayName(name[, requireFullMatch])")[0];
            var full = args.Count > 1 && args[1].Type == DataType.Boolean && args[1].Boolean;

            var found = Player.GetByDisplayName(name, full);
            return found is null ? DynValue.Nil : cache.Wrap(found);
        });

        t["tryGetPlayersByName"] = DynValue.NewCallback((_, args) =>
        {
            var name = LuaArgs.CollectStrings(args, 0, 1, "player.tryGetPlayersByName(name)")[0];

            return Player.TryGetPlayersByName(name, out var players)
                ? DynValue.NewTable(WrapAll(s, cache, players))
                : DynValue.Nil;
        });

        t["random"] = DynValue.NewCallback((_, _) =>
        {
            var list = Player.ReadyList.ToList();
            if (list.Count == 0)
                return DynValue.Nil;

            var idx = UnityEngine.Random.Range(0, list.Count);
            return cache.Wrap(list[idx]);
        });

        return t;
    }

    private static Table WrapAll(Script script, LuaObjectCache cache, IEnumerable<Player> players)
    {
        var arr = new Table(script);
        var i = 1;

        foreach (var p in players)
            arr[i++] = cache.Wrap(p);

        return arr;
    }

    private static bool MatchesId(Player p, string id)
    {
        try
        {
            if (uint.TryParse(id, out var nid))
            {
                if (p.NetworkId == nid)
                    return true;
            }
            if (p.UserId == id)
                return true;
            if (p.Nickname == id)
                return true;
        }
        catch (Exception ex)
        {
            LuaDiagnostics.WarnOnce($"player: 按标识 '{id}' 比对玩家失败，该玩家视为不匹配", ex);
        }

        return false;
    }
}
