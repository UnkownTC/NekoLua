using LabApi.Features.Wrappers;
using MoonSharp.Interpreter;
using NekoLua.Api;
using NekoLua.Api.TypeScanner;
using NekoLua.Loader;
using NekoLua.Registers;
using SecretLabNAudio.Core;
using UnityEngine;

namespace NekoLua.SecretLabNAudioProxy.Proxies;

internal class LuaSoundProxy : IRegisterLua
{
    public string Name { get; } = "sound";
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);
        var cache = new LuaObjectCache(s);

        t["playGlobal"] = DynValue.NewCallback((_, a) => PlayGlobal(mod, cache, a));
        t["playLocal"] = DynValue.NewCallback((_, a) => PlayLocal(mod, cache, a));

        return t;
    }
    private DynValue PlayGlobal(LuaModInstance mod, LuaObjectCache cache, CallbackArguments args)
    {
        if (args.Count < 1 || args[0].Type != DataType.String)
            throw new ScriptRuntimeException($"{Name}.playGlobal(path[, options])");

        var path = Resolve(mod, args[0].String);
        var opts = args.Count > 1 && args[1].Type == DataType.Table ? args[1].Table : null;
        var loop = OptBool(opts, "loop", false);
        var volume = (float)OptNumber(opts, "volume", 1.0);
        var parent = OptTransform(opts, "parent");
        var filter = opts?.Get("filter");

        try
        {
            AudioPlayer player;

            switch (filter?.Type)
            {
                case null:
                case DataType.Nil:
                case DataType.Void:
                    player = SoundManager.PlayGlobally(path, parent, loop, volume);
                    break;

                case DataType.Function:
                    {
                        var f = BuildLuaFilter(mod, cache, filter);
                        player = SoundManager.PlayGlobally(
                            path, p => f(p), parent, loop, volume);
                        break;
                    }

                case DataType.Table:
                    player = SoundManager.PlayGlobally(
                        path, ExtractPlayers(filter.Table), parent, loop, volume);
                    break;

                default:
                    throw new ScriptRuntimeException(
                        $"{Name}.playGlobal: options.filter 需要函数或玩家数组");
            }

            return cache.Wrap(player);
        }
        catch (ScriptRuntimeException) { throw; }
        catch (Exception ex)
        {
            throw new ScriptRuntimeException($"{Name}.playGlobal: {ex.Message}");
        }
    }
    private DynValue PlayLocal(LuaModInstance mod, LuaObjectCache cache, CallbackArguments args)
    {
        if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.Table)
            throw new ScriptRuntimeException($"{Name}.playLocal(path, position[, options])");

        var path = Resolve(mod, args[0].String);
        var pos = LuaVector.UnpackVector3Args(args[1], $"{Name}.playLocal");
        var opts = args.Count > 2 && args[2].Type == DataType.Table ? args[2].Table : null;
        var loop = OptBool(opts, "loop", false);
        var volume = (float)OptNumber(opts, "volume", 1.0);
        var spatial = OptBool(opts, "spatial", true);
        var minDistance = (float)OptNumber(opts, "minDistance", 1.0);
        var maxDistance = (float)OptNumber(opts, "maxDistance", 15.0);
        var parent = OptTransform(opts, "parent");
        var filter = opts?.Get("filter");

        try
        {
            AudioPlayer player;

            switch (filter?.Type)
            {
                case null:
                case DataType.Nil:
                case DataType.Void:
                    player = SoundManager.PlayLocally(
                        path, pos, parent, spatial, minDistance, maxDistance, loop, volume);
                    break;

                case DataType.Function:
                    {
                        var f = BuildLuaFilter(mod, cache, filter);
                        player = SoundManager.PlayLocally(
                            path, pos, p => f(p), parent, spatial, minDistance, maxDistance, loop, volume);
                        break;
                    }

                default:
                    throw new ScriptRuntimeException(
                        $"{Name}.playLocal: options.filter 只支持函数");
            }

            return cache.Wrap(player);
        }
        catch (ScriptRuntimeException) { throw; }
        catch (Exception ex)
        {
            throw new ScriptRuntimeException($"{Name}.playLocal: {ex.Message}");
        }
    }
    private Func<Player, bool> BuildLuaFilter(
        LuaModInstance mod, LuaObjectCache cache, DynValue luaFn)
    {
        return player =>
        {
            if (player is null) return false;

            try
            {
                var r = luaFn.Function.Call(cache.Wrap(player));
                return r.Type == DataType.Boolean && r.Boolean;
            }
            catch (InterpreterException ex)
            {
                LabApi.Features.Console.Logger.Error(
                    $"[{mod.Name}] {Name} filter 回调异常: {ex.DecoratedMessage}");
                return false;
            }
            catch
            {
                return false;
            }
        };
    }
    private static ICollection<Player> ExtractPlayers(Table tbl)
    {
        var list = new List<Player>();
        var n = tbl.Length;

        for (var i = 1; i <= n; i++)
        {
            var v = tbl.Get(i);
            if (v.Type != DataType.Table) continue;

            if (LuaObjectProxy.Unwrap(v.Table) is Player p)
                list.Add(p);
        }

        return list;
    }
    private static bool OptBool(Table? opts, string key, bool fallback)
    {
        if (opts is null) return fallback;
        var v = opts.Get(key);
        return v.Type == DataType.Boolean ? v.Boolean : fallback;
    }

    private static double OptNumber(Table? opts, string key, double fallback)
    {
        if (opts is null) return fallback;
        var v = opts.Get(key);
        return v.Type == DataType.Number ? v.Number : fallback;
    }
    private static Transform? OptTransform(Table? opts, string key)
    {
        if (opts is null) return null;

        var v = opts.Get(key);
        return v.Type != DataType.Table
            ? null
            : LuaObjectProxy.Unwrap(v.Table) switch
            {
                Transform tf => tf,
                GameObject go => go.transform,
                _ => null,
            };
    }
    private string Resolve(LuaModInstance mod, string input)
    {
        return LuaPathGuard.Resolve(mod, input, Name);
    }
}