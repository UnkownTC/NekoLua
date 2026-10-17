using LabApi.Features.Wrappers;
using MoonSharp.Interpreter;
using NekoLua.Api;
using NekoLua.Api.TypeScanner;
using NekoLua.Loader;
using NekoLua.Registers;
using LabCamera = LabApi.Features.Wrappers.Camera;

namespace NekoLua.Proxies.Game;

/// <summary>
/// camera 表（SCP-079 摄像头）：
///
///   静态：
///     camera.list()                     -- 全部摄像头
///     camera.count()                    -- 数量
///     camera.getByRoom(room)            -- 指定房间内的摄像头
///     camera.findNear(position, radius) -- 半径内
///
///   实例（走通用代理）：
///     position / rotation（读写）/ room / zoom / isBeingUsed（读写）/
///     base / gameObject
/// </summary>
internal class LuaCameraProxy : IRegisterLua
{
    public string Name { get; } = "camera";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);
        var cache = new LuaObjectCache(s);

        t["list"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(s);
            var i = 1;
            foreach (var c in LabCamera.List)
                arr[i++] = cache.Wrap(c);
            return DynValue.NewTable(arr);
        });

        t["count"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(LabCamera.List.Count));

        t["getByRoom"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Table)
                throw new ScriptRuntimeException("camera.getByRoom(room)");

            var obj = LuaObjectProxy.Unwrap(args[0].Table);
            if (obj is not Room room)
                throw new ScriptRuntimeException("camera.getByRoom: 不是有效的 Room 代理");

            var arr = new Table(s);
            var i = 1;

            foreach (var c in LabCamera.List)
            {
                try { if (c.Room == room) arr[i++] = cache.Wrap(c); }
                catch { }
            }
            return DynValue.NewTable(arr);
        });

        t["findNear"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[1].Type != DataType.Number)
                throw new ScriptRuntimeException("camera.findNear(position, radius)");

            var pos = LuaVector.UnpackVector3Args(args[0], "camera.findNear");
            var radius = (float)args[1].Number;

            var arr = new Table(s);
            var i = 1;

            foreach (var c in LabCamera.List)
            {
                try
                {
                    if (UnityEngine.Vector3.Distance(c.Position, pos) <= radius)
                        arr[i++] = cache.Wrap(c);
                }
                catch { }
            }
            return DynValue.NewTable(arr);
        });

        return t;
    }
}