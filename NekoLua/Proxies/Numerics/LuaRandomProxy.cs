using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;

using Random = UnityEngine.Random;

namespace NekoLua.Proxies.Numerics;

/// <summary>
/// random 表（基于 UnityEngine.Random）：
///   random.int(min, max)            -- 闭区间
///   random.float(min, max)
///   random.value                    -- [0, 1)
///   random.pick(table)              -> 元素 | nil
/// </summary>
public class LuaRandomProxy : IRegisterLua
{
    public string Name { get; } = "random";

    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);

        t["int"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2)
                throw new ScriptRuntimeException("random.int(min, max)");

            var min = (int)args[0].Number;
            var max = (int)args[1].Number;
            if (min > max)
                (min, max) = (max, min);

            return DynValue.NewNumber(Random.Range(min, max + 1));
        });

        t["float"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2)
                throw new ScriptRuntimeException("random.float(min, max)");

            var min = (float)args[0].Number;
            var max = (float)args[1].Number;
            if (min > max)
                (min, max) = (max, min);

            return DynValue.NewNumber(Random.Range(min, max));
        });

        t["value"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(Random.value));

        t["pick"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Table)
                throw new ScriptRuntimeException("random.pick(table)");

            var tbl = args[0].Table;
            var len = tbl.Length;
            return len == 0 ? DynValue.Nil : tbl.Get(Random.Range(1, len + 1));
        });

        return t;
    }
}
