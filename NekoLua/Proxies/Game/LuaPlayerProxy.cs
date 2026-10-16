using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.GameContents;

/// <summary>
/// player 表：
///   player.list() / readyList() / count() / readyCount()
///   player.get(id)                  -- 按 networkId / userId / nickname 查
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

        t["list"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;
            foreach (var p in Player.List)
                arr[i++] = cache.Wrap(p);
            return DynValue.NewTable(arr);
        });

        t["readyList"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;
            foreach (var p in Player.ReadyList)
                arr[i++] = cache.Wrap(p);
            return DynValue.NewTable(arr);
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
        catch { }

        return false;
    }
}
