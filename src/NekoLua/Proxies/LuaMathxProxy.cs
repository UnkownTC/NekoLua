using MoonSharp.Interpreter;

using NekoLua.Loader;
using NekoLua.Registers;

using NorthwoodLib.Pools;

namespace NekoLua.Proxies;

/// <summary>
/// mathx 表：
///   插值：lerp / invLerp / remap / clamp / clamp01 / smoothstep / smootherstep
///   缓动：easeInQuad / easeOutQuad / easeInOutQuad / easeInCubic /
///         easeOutCubic / easeInOutCubic / easeInSine / easeOutSine /
///         easeInOutSine / easeOutElastic
///   距离：distance2D / distance3D / manhattan / chebyshev
///   统计：sum / mean / min / max / median / variance / stdev
///   角度：deg / rad / wrapAngle / deltaAngle
///   其他：sign / fract / repeat / pingPong
/// </summary>
public class LuaMathxProxy : IRegisterLua
{
    public string Name { get; } = "mathx";
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["lerp"] = Tern((a, b, k) => a + (b - a) * k,
            $"{Name}.lerp(a, b, t)");

        t["invLerp"] = Tern((a, b, v) => b == a ? 0 : (v - a) / (b - a),
            $"{Name}.invLerp(a, b, v)");

        t["remap"] = Quat((i1, i2, o1, o2, v) =>
        {
            if (i2 == i1)
                return o1;
            var k = (v - i1) / (i2 - i1);
            return o1 + (o2 - o1) * k;
        }, $"{Name}.remap(i1, i2, o1, o2, v)");

        t["clamp"] = Tern((v, lo, hi) =>
        {
            if (lo > hi)
                (lo, hi) = (hi, lo);
            if (v < lo)
                return lo;
            if (v > hi)
                return hi;
            return v;
        }, $"{Name}.clamp(v, lo, hi)");

        t["clamp01"] = Un(v => v < 0 ? 0 : v > 1 ? 1 : v,
            $"{Name}.clamp01(v)");

        t["smoothstep"] = Tern((e0, e1, x) =>
        {
            var tt = (x - e0) / (e1 - e0);
            tt = tt < 0 ? 0 : tt > 1 ? 1 : tt;
            return tt * tt * (3 - 2 * tt);
        }, $"{Name}.smoothstep(e0, e1, x)");

        t["smootherstep"] = Tern((e0, e1, x) =>
        {
            var tt = (x - e0) / (e1 - e0);
            tt = tt < 0 ? 0 : tt > 1 ? 1 : tt;
            return tt * tt * tt * (tt * (tt * 6 - 15) + 10);
        }, $"{Name}.smootherstep(e0, e1, x)");

        t["easeInQuad"] = Ease(k => k * k);
        t["easeOutQuad"] = Ease(k => 1 - (1 - k) * (1 - k));
        t["easeInOutQuad"] = Ease(k => k < 0.5 ? 2 * k * k : 1 - Math.Pow(-2 * k + 2, 2) / 2);
        t["easeInCubic"] = Ease(k => k * k * k);
        t["easeOutCubic"] = Ease(k => 1 - Math.Pow(1 - k, 3));
        t["easeInOutCubic"] = Ease(k => k < 0.5 ? 4 * k * k * k : 1 - Math.Pow(-2 * k + 2, 3) / 2);
        t["easeInSine"] = Ease(k => 1 - Math.Cos(k * Math.PI / 2));
        t["easeOutSine"] = Ease(k => Math.Sin(k * Math.PI / 2));
        t["easeInOutSine"] = Ease(k => -(Math.Cos(Math.PI * k) - 1) / 2);
        t["easeOutElastic"] = Ease(k =>
        {
            if (k == 0 || k == 1)
                return k;
            var c4 = 2 * Math.PI / 3;
            return Math.Pow(2, -10 * k) * Math.Sin((k * 10 - 0.75) * c4) + 1;
        });

        t["distance2D"] = Quat((x1, y1, x2, y2, _) =>
            Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1)),
            $"{Name}.distance2D(x1, y1, x2, y2)");

        t["distance3D"] = Penta((x1, y1, z1, x2, y2, z2) =>
            Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1) + (z2 - z1) * (z2 - z1)),
            $"{Name}.distance3D(x1, y1, z1, x2, y2, z2)");

        t["manhattan"] = Bin((a, b) => Math.Abs(a) + Math.Abs(b),
            $"{Name}.manhattan(x, y)");

        t["chebyshev"] = Bin((a, b) => Math.Max(Math.Abs(a), Math.Abs(b)),
            $"{Name}.chebyshev(x, y)");

        t["sum"] = Stat(xs => xs.Sum(), $"{Name}.sum(table)");
        t["mean"] = Stat(xs => xs.Average(), $"{Name}.mean(table)");
        t["min"] = Stat(xs => xs.Min(), $"{Name}.min(table)");
        t["max"] = Stat(xs => xs.Max(), $"{Name}.max(table)");

        t["median"] = Stat(xs =>
        {
            var arr = xs.OrderBy(x => x).ToArray();
            if (arr.Length == 0)
                return 0;
            return arr.Length % 2 == 1
                ? arr[arr.Length / 2]
                : (arr[arr.Length / 2 - 1] + arr[arr.Length / 2]) / 2;
        }, $"{Name}.median(table)");

        t["variance"] = Stat(xs =>
        {
            if (xs.Count == 0)
                return 0;
            var m = xs.Average();
            return xs.Average(x => (x - m) * (x - m));
        }, $"{Name}.variance(table)");

        t["stdev"] = Stat(xs =>
        {
            if (xs.Count == 0)
                return 0;
            var m = xs.Average();
            return Math.Sqrt(xs.Average(x => (x - m) * (x - m)));
        }, $"{Name}.stdev(table)");

        t["deg"] = Un(r => r * 180 / Math.PI, $"{Name}.deg(rad)");
        t["rad"] = Un(d => d * Math.PI / 180, $"{Name}.rad(deg)");

        t["wrapAngle"] = Un(a =>
        {
            // 用取模替代逐次减 360 的循环：1e308 / math.huge 会让循环永不收敛冻结主线程（审查 P2-6）
            var w = a % 360;
            if (w > 180)
                w -= 360;
            else if (w < -180)
                w += 360;
            return w;
        }, $"{Name}.wrapAngle(deg)");

        t["deltaAngle"] = Bin((a, b) =>
        {
            var d = (b - a) % 360;
            if (d > 180)
                d -= 360;
            if (d < -180)
                d += 360;
            return d;
        }, $"{Name}.deltaAngle(a, b)");

        t["sign"] = Un(x => Math.Sign(x), $"{Name}.sign(x)");
        t["fract"] = Un(x => x - Math.Floor(x), $"{Name}.fract(x)");

        t["repeat"] = Bin((x, len) =>
        {
            if (len == 0)
                return 0;
            return x - Math.Floor(x / len) * len;
        }, $"{Name}.repeat(x, len)");

        t["pingPong"] = Bin((x, len) =>
        {
            if (len == 0)
                return 0;
            var k = x - Math.Floor(x / (len * 2)) * len * 2;
            return k < len ? k : len * 2 - k;
        }, $"{Name}.pingPong(x, len)");

        return t;
    }
    static DynValue Un(Func<double, double> f, string sig)
        => DynValue.NewCallback((_, a) =>
        {
            if (a.Count < 1)
                throw new ScriptRuntimeException(sig);
            return DynValue.NewNumber(f(a[0].Number));
        });

    static DynValue Bin(Func<double, double, double> f, string sig)
        => DynValue.NewCallback((_, a) =>
        {
            if (a.Count < 2)
                throw new ScriptRuntimeException(sig);
            return DynValue.NewNumber(f(a[0].Number, a[1].Number));
        });

    static DynValue Tern(Func<double, double, double, double> f, string sig)
        => DynValue.NewCallback((_, a) =>
        {
            if (a.Count < 3)
                throw new ScriptRuntimeException(sig);
            return DynValue.NewNumber(f(a[0].Number, a[1].Number, a[2].Number));
        });

    static DynValue Quat(Func<double, double, double, double, double, double> f, string sig)
        => DynValue.NewCallback((_, a) =>
        {
            if (a.Count < 5)
                throw new ScriptRuntimeException(sig);
            return DynValue.NewNumber(f(
                a[0].Number, a[1].Number, a[2].Number, a[3].Number, a[4].Number));
        });

    static DynValue Penta(Func<double, double, double, double, double, double, double> f, string sig)
        => DynValue.NewCallback((_, a) =>
        {
            if (a.Count < 6)
                throw new ScriptRuntimeException(sig);
            return DynValue.NewNumber(f(
                a[0].Number, a[1].Number, a[2].Number,
                a[3].Number, a[4].Number, a[5].Number));
        });

    static DynValue Ease(Func<double, double> f)
        => DynValue.NewCallback((_, a) =>
        {
            if (a.Count < 1)
                throw new ScriptRuntimeException("mathx.ease(t)");
            var k = a[0].Number;
            k = k < 0 ? 0 : k > 1 ? 1 : k;
            return DynValue.NewNumber(f(k));
        });

    static DynValue Stat(Func<List<double>, double> f, string sig)
        => DynValue.NewCallback((_, a) =>
        {
            if (a.Count < 1 || a[0].Type != DataType.Table)
                throw new ScriptRuntimeException(sig);

            var arr = a[0].Table;
            var list = ListPool<double>.Shared.Rent();
            for (var i = 1; i <= arr.Length; i++)
            {
                var v = arr.Get(i);
                if (v.Type == DataType.Number)
                    list.Add(v.Number);
            }

            if (list.Count == 0)
                return DynValue.NewNumber(0);

            var result = DynValue.NewNumber(f(list));

            ListPool<double>.Shared.Return(list);

            return result;
        });
}
