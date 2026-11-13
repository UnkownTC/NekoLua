using LabApi.Features.Wrappers;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using UnityEngine;

namespace NekoLua.Proxies.GameContents;

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
                catch (Exception ex)
                {
                    LuaDiagnostics.WarnOnce("window.findNear: 读取窗户坐标失败，该窗户已跳过", ex);
                }
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
                catch (Exception ex)
                {
                    LuaDiagnostics.WarnOnce("window.damageAll: 对窗户造成伤害失败，已跳过", ex);
                }
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
                catch (Exception ex)
                {
                    LuaDiagnostics.WarnOnce("window.breakAll: 破坏窗户失败，已跳过", ex);
                }
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
                    n++;   // 审查 P3-8：原实现漏计数，恒返回 0
                }
                catch (Exception ex)
                {
                    LuaDiagnostics.WarnOnce("window.repairAll: 修复窗户失败，已跳过", ex);
                }
            }

            return DynValue.NewNumber(n);
        });

        t["repair"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Table)
                throw new ScriptRuntimeException("window.repair(w)");

            // 审查 P3-9：原实现依赖 DynValue 引用同一性（cache.Wrap 每次重建代理 → 必然失配），
            // 改用 LuaObjectProxy.Unwrap 取回底层包装对象后按引用比较。
            if (LuaObjectProxy.Unwrap(args[0].Table) is not Window target)
                throw new ScriptRuntimeException("window.repair: 参数不是 window 代理");

            try
            {
                if (target.IsDestroyed || target.Base is null)
                    return DynValue.False;

                target.Base.Health = RepairHealth;
                return DynValue.True;
            }
            catch
            {
                return DynValue.False;
            }
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
                catch (Exception ex)
                {
                    LuaDiagnostics.WarnOnce($"window: 按条件筛选窗户失败（谓词 {predicate.Method.Name}），该窗户已跳过", ex);
                }
            }

            return DynValue.NewTable(arr);
        });
    }
}
