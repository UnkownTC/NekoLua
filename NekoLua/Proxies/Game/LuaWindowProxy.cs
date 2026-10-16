using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;
using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using UnityEngine;

namespace NekoLua.Proxies.Game;

/// <summary>
/// window 表：
///   window.list() / count()
///   window.broken() / intact()
///   window.findNear(position, radius)
///   window.damageAll(amount) / breakAll() / repairAll()
///   window.repair(w)
///
/// 实例：w.isBroken / isDestroyed / health（读写）
///      position / rotation / base / transform / gameObject
/// </summary>
public class LuaWindowProxy : IRegisterLua
{
    public string Name { get; } = "window";
    private const float RepairHealth = 50f;
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);
        var cache = new LuaObjectCache(s);

        t["list"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;
            foreach (var w in Window.List)
                arr[i++] = cache.Wrap(w);
            return DynValue.NewTable(arr);
        });

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(Window.List.Count));

        t["broken"] = FilterBy(s, cache, w => w.IsBroken);
        t["intact"] = FilterBy(s, cache, w => !w.IsBroken);

        t["findNear"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[1].Type != DataType.Number)
                throw new ScriptRuntimeException("window.findNear(position, radius)");

            var pos = LuaVector.UnpackV3Args(args[0], "window.findNear");
            var radius = (float)args[1].Number;

            var arr = new Table(s);
            var i = 1;

            foreach (var w in Window.List)
            {
                try
                {
                    if (w.IsDestroyed)
                        continue;
                    if (Vector3.Distance(w.Position, pos) <= radius)
                        arr[i++] = cache.Wrap(w);
                }
                catch { }
            }

            return DynValue.NewTable(arr);
        });

        t["damageAll"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("window.damageAll(amount)");

            var amount = (float)args[0].Number;
            if (amount < 0)
                throw new ScriptRuntimeException("window.damageAll: amount 必须 >= 0");

            var n = 0;

            foreach (var w in Window.List)
            {
                try
                {
                    if (w.IsDestroyed || w.IsBroken)
                        continue;
                    w.Health = Math.Max(0, w.Health - amount);
                    n++;
                }
                catch { }
            }

            return DynValue.NewNumber(n);
        });

        t["breakAll"] = DynValue.NewCallback((_, _) =>
        {
            var n = 0;

            foreach (var w in Window.List)
            {
                try
                {
                    if (w.IsDestroyed || w.IsBroken)
                        continue;
                    w.Health = 0f;
                    n++;
                }
                catch { }
            }

            return DynValue.NewNumber(n);
        });

        t["repairAll"] = DynValue.NewCallback((_, _) =>
        {
            var n = 0;

            foreach (var w in Window.List)
            {
                try
                {
                    if (w.IsDestroyed)
                        continue;

                    var baseWindow = w.Base;
                    if (baseWindow is null)
                        continue;

                    baseWindow.Health = RepairHealth;

                }
                catch { }
            }

            return DynValue.NewNumber(n);
        });

        t["repair"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Table)
                throw new ScriptRuntimeException("window.repair(w)");

            var baseV = args[0].Table.Get("base");
            if (baseV.Type is not DataType.UserData and not DataType.Table)
                throw new ScriptRuntimeException("window.repair: 参数无效");

            var target = args[0];
            foreach (var w in Window.List)
            {
                try
                {
                    if (w.IsDestroyed)
                        continue;

                    var wrapped = cache.Wrap(w);
                    if (wrapped != target)
                        continue;

                    var prop = w.Base.GetType().GetProperty("Health");
                    if (prop is { CanWrite: true })
                    {
                        prop.SetValue(w.Base, RepairHealth);
                        return DynValue.True;
                    }
                }
                catch { }
            }

            return DynValue.False;
        });

        return t;
    }
    private static DynValue FilterBy(
        Script s,
        LuaObjectCache cache,
        Func<Window, bool> predicate)
    {
        return DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;

            foreach (var w in Window.List)
            {
                try
                {
                    if (predicate(w))
                        arr[i++] = cache.Wrap(w);
                }
                catch { }
            }

            return DynValue.NewTable(arr);
        });
    }
}
