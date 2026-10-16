using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;
using NorthwoodLib.Pools;

namespace NekoLua.Proxies.Numerics;

/// <summary>
/// 扩展 Lua 原生 math 表（追加，不替换）：
///   插值：lerp / invLerp / remap / clamp
///   缓动：smoothstep / smootherstep / easeIn* / easeOut* / easeInOut*
///   距离：distance2D / distance3D
///   统计：sum / mean / median / variance / stdev
///   极值：min / max —— 单 table 参数走表计算，变参走原生
///
/// 原生 math.*（floor / ceil / abs / sin / cos / sqrt / random / min / max / …）
/// 全部保留。
/// </summary>
public class LuaMathProxy : IRegisterLua
{
    public string Name { get; } = "math";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = s.Globals.Get("math").Table;

        var nativeMin = t.Get("min");
        var nativeMax = t.Get("max");


        t["lerp"] = Tern((a, b, k) => a + ((b - a) * k),
            $"{Name}.lerp(a, b, t)");

        t["invLerp"] = Tern((a, b, v) => b == a ? 0 : (v - a) / (b - a),
            $"{Name}.invLerp(a, b, v)");

        t["remap"] = Quat((i1, i2, o1, o2, v) =>
        {
            return i2 == i1 ? o1 : o1 + ((o2 - o1) * (v - i1) / (i2 - i1));
        }, $"{Name}.remap(i1, i2, o1, o2, v)");

        t["clamp"] = Tern((v, lo, hi) =>
        {
            if (lo > hi) (lo, hi) = (hi, lo);
            return v < lo ? lo : v > hi ? hi : v;
        }, $"{Name}.clamp(v, lo, hi)");

        t["smoothstep"] = Tern((e0, e1, x) =>
        {
            var k = Clamp01((x - e0) / (e1 - e0));
            return k * k * (3 - (2 * k));
        }, $"{Name}.smoothstep(e0, e1, x)");

        t["smootherstep"] = Tern((e0, e1, x) =>
        {
            var k = Clamp01((x - e0) / (e1 - e0));
            return k * k * k * ((k * ((k * 6) - 15)) + 10);
        }, $"{Name}.smootherstep(e0, e1, x)");

        t["easeInQuad"] = Ease(Name, k => k * k);
        t["easeOutQuad"] = Ease(Name, k => 1 - ((1 - k) * (1 - k)));
        t["easeInOutQuad"] = Ease(Name, k => k < 0.5 ? 2 * k * k : 1 - (Math.Pow((-2 * k) + 2, 2) / 2));
        t["easeInCubic"] = Ease(Name, k => k * k * k);
        t["easeOutCubic"] = Ease(Name, k => 1 - Math.Pow(1 - k, 3));
        t["easeInOutCubic"] = Ease(Name, k => k < 0.5 ? 4 * k * k * k : 1 - (Math.Pow((-2 * k) + 2, 3) / 2));
        t["easeInSine"] = Ease(Name, k => 1 - Math.Cos(k * Math.PI / 2));
        t["easeOutSine"] = Ease(Name, k => Math.Sin(k * Math.PI / 2));
        t["easeInOutSine"] = Ease(Name, k => -(Math.Cos(Math.PI * k) - 1) / 2);
        t["easeOutElastic"] = Ease(Name, k =>
        {
            if (k <= 0) return 0;
            if (k >= 1) return 1;
            const double c4 = 2 * Math.PI / 3;
            return (Math.Pow(2, -10 * k) * Math.Sin(((k * 10) - 0.75) * c4)) + 1;
        });

        t["distance2D"] = Quat((x1, y1, x2, y2, _) =>
        {
            var dx = x2 - x1;
            var dy = y2 - y1;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }, $"{Name}.distance2D(x1, y1, x2, y2)");

        t["distance3D"] = Penta((x1, y1, z1, x2, y2, z2) =>
        {
            var dx = x2 - x1;
            var dy = y2 - y1;
            var dz = z2 - z1;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }, $"{Name}.distance3D(x1, y1, z1, x2, y2, z2)");

        t["sum"] = Stat(xs => xs.Sum(), $"{Name}.sum(t)");
        t["mean"] = Stat(xs => xs.Average(), $"{Name}.mean(t)");
        t["median"] = Stat(Median, $"{Name}.median(t)");
        t["variance"] = Stat(Variance, $"{Name}.variance(t)");
        t["stdev"] = Stat(xs => Math.Sqrt(Variance(xs)), $"{Name}.stdev(t)");

        t["min"] = SmartExtreme(nativeMin, $"{Name}.min(t)");
        t["max"] = SmartExtreme(nativeMax, $"{Name}.max(t)");

        return t;
    }
    static double Median(List<double> xs)
    {
        if (xs.Count == 0) return 0;

        var arr = xs.OrderBy(x => x).ToArray();
        var n = arr.Length;

        return n % 2 == 1
            ? arr[n / 2]
            : (arr[(n / 2) - 1] + arr[n / 2]) / 2;
    }

    static double Variance(List<double> xs)
    {
        if (xs.Count == 0) return 0;

        var m = xs.Average();
        return xs.Average(x => (x - m) * (x - m));
    }
    static DynValue SmartExtreme(DynValue native, string __)
    {
        return DynValue.NewCallback((_, a) =>
            {
                if (a.Count == 1 && a[0].Type == DataType.Table)
                {
                    var arr = a[0].Table;
                    var list = ListPool<double>.Shared.Rent();
                    try
                    {
                        for (var i = 1; i <= arr.Length; i++)
                        {
                            var v = arr.Get(i);
                            if (v.Type == DataType.Number)
                                list.Add(v.Number);
                        }

                        return list.Count == 0
                            ? DynValue.NewNumber(0)
                            : DynValue.NewNumber(list.Min());
                    }
                    finally
                    {
                        ListPool<double>.Shared.Return(list);
                    }
                }

                // math.min(1, 2, 3)  →  原生
                var args = new DynValue[a.Count];
                for (var i = 0; i < a.Count; i++) args[i] = a[i];
                return native.Function.Call(args);
            });
    }

    // ================================================================
    // 参数打包
    // ================================================================

    static double Clamp01(double v)
    {
        return v < 0 ? 0 : v > 1 ? 1 : v;
    }

    static DynValue Tern(Func<double, double, double, double> f, string sig)
    {
        return DynValue.NewCallback((_, a) =>
            {
                return a.Count < 3 ? throw new ScriptRuntimeException(sig) : DynValue.NewNumber(f(a[0].Number, a[1].Number, a[2].Number));
            });
    }

    static DynValue Quat(Func<double, double, double, double, double, double> f, string sig)
    {
        return DynValue.NewCallback((_, a) =>
            {
                return a.Count < 5
                    ? throw new ScriptRuntimeException(sig)
                    : DynValue.NewNumber(f(
                    a[0].Number, a[1].Number, a[2].Number, a[3].Number, a[4].Number));
            });
    }

    static DynValue Penta(Func<double, double, double, double, double, double, double> f, string sig)
    {
        return DynValue.NewCallback((_, a) =>
            {
                return a.Count < 6
                    ? throw new ScriptRuntimeException(sig)
                    : DynValue.NewNumber(f(
                    a[0].Number, a[1].Number, a[2].Number,
                    a[3].Number, a[4].Number, a[5].Number));
            });
    }

    static DynValue Ease(string name, Func<double, double> f)
    {
        return DynValue.NewCallback((_, a) =>
            {
                return a.Count < 1 ? throw new ScriptRuntimeException($"{name}.ease(t)") : DynValue.NewNumber(f(Clamp01(a[0].Number)));
            });
    }

    static DynValue Stat(Func<List<double>, double> f, string sig)
    {
        return DynValue.NewCallback((_, a) =>
            {
                if (a.Count < 1 || a[0].Type != DataType.Table)
                    throw new ScriptRuntimeException(sig);

                var arr = a[0].Table;
                var list = ListPool<double>.Shared.Rent();

                try
                {
                    for (var i = 1; i <= arr.Length; i++)
                    {
                        var v = arr.Get(i);
                        if (v.Type == DataType.Number)
                            list.Add(v.Number);
                    }

                    return list.Count == 0
                        ? DynValue.NewNumber(0)
                        : DynValue.NewNumber(f(list));
                }
                finally
                {
                    ListPool<double>.Shared.Return(list);
                }
            });
    }
}