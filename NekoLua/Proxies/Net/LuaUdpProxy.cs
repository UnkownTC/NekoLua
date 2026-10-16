using System.Net.Sockets;
using System.Text;

using LabApi.Features.Console;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Net;

/// <summary>
/// udp 表（由 Config.EnableNetwork 控制）：
///   local c = udp.create([bindPort])      -- bindPort 省略时随机
///   c.onReceive = function(data, ip, port)
///   c.onError = function(err)
///   c:send(host, port, text) / sendBytes(host, port, table)
///   c:close()
///   c.isOpen / c.isClosed
/// </summary>
public class LuaUdpProxy : IRegisterLua
{
    public string Name { get; } = "udp";
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["create"] = DynValue.NewCallback((_, args) =>
        {
            if (!MainPlugin.CacheConfig.EnableNetwork)
                throw new ScriptRuntimeException("udp.create: 网络功能未启用（Config.EnableNetwork）");

            int? bindPort = null;

            if (args.Count >= 1 && args[0].Type == DataType.Number)
            {
                bindPort = (int)args[0].Number;
                if (bindPort < 0 || bindPort > 65535)
                    throw new ScriptRuntimeException("udp.create: port 超范围");
            }

            try
            {
                var client = bindPort.HasValue
                    ? new UdpClient(bindPort.Value)
                    : new UdpClient(0);

                var conn = new UdpConnection(mod, s, client);

                if (!LuaNetworkManager.TryRegister(mod, conn))
                {
                    client.Dispose();
                    throw new ScriptRuntimeException("udp.create: 已达连接上限");
                }

                conn.StartReceive();
                return DynValue.NewTable(conn.GetTable());
            }
            catch (ScriptRuntimeException) { throw; }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"udp.create: {ex.Message}");
            }
        });

        return t;
    }
    internal sealed class UdpConnection(LuaModInstance mod, Script script, UdpClient client) : IDisposable
    {
        // 待处理报文字节上限：主线程每帧最多消费 64 条回调，恶意对端用 64KB 报文灌包
        // 会把队列打到数百 MB 驻留内存；按字节限幅后超限直接丢包（审查 P2-19）
        private const long MaxPendingBytes = 8 * 1024 * 1024;

        private readonly LuaModInstance _mod = mod;
        private readonly Script _script = script;
        private readonly UdpClient _client = client;

        private long _pendingBytes;
        private DynValue? _onReceive;
        private DynValue? _onError;

        private Table? _table;
        private volatile bool _closed;
        public Table GetTable()
        {
            if (_table != null)
                return _table;

            var t = new Table(_script);
            var meta = new Table(_script);

            t["send"] = DynValue.NewCallback((_, a) => Send(a, isBytes: false));
            t["sendBytes"] = DynValue.NewCallback((_, a) => Send(a, isBytes: true));
            t["close"] = DynValue.NewCallback((_, _) => { Dispose(); return DynValue.Nil; });

            meta["__index"] = DynValue.NewCallback((_, args) =>
            {
                if (args.Count < 2 || args[1].Type != DataType.String)
                    return DynValue.Nil;
                return args[1].String switch
                {
                    "isOpen" => DynValue.NewBoolean(!_closed),
                    "isClosed" => DynValue.NewBoolean(_closed),
                    "onReceive" => _onReceive ?? DynValue.Nil,
                    "onError" => _onError ?? DynValue.Nil,
                    _ => t.Get(args[1].String),
                };
            });

            meta["__newindex"] = DynValue.NewCallback((_, args) =>
            {
                if (args.Count < 3 || args[1].Type != DataType.String)
                    return DynValue.Nil;

                var name = args[1].String;
                var fn = args[2];

                switch (name)
                {
                    case "onReceive":
                        _onReceive = NormFunc(fn);
                        break;
                    case "onError":
                        _onError = NormFunc(fn);
                        break;
                    default:
                        throw new ScriptRuntimeException($"udp: 未知字段 '{name}'");
                }

                return DynValue.Nil;
            });

            t.MetaTable = meta;
            _table = t;
            return t;
        }

        private static DynValue? NormFunc(DynValue v)
            => v.Type == DataType.Function ? v : null;

        public void StartReceive() => _ = ReceiveLoopAsync();

        private async Task ReceiveLoopAsync()
        {
            try
            {
                while (!_closed)
                {
                    var result = await _client.ReceiveAsync();

                    var size = result.Buffer.Length;
                    if (Interlocked.Read(ref _pendingBytes) + size > MaxPendingBytes)
                        continue;

                    var text = Encoding.UTF8.GetString(result.Buffer);
                    var ip = result.RemoteEndPoint.Address.ToString();
                    var port = result.RemoteEndPoint.Port;

                    _ = Interlocked.Add(ref _pendingBytes, size);
                    PostReceive(() =>
                    {
                        try
                        {
                            if (_onReceive is null)
                                return;
                            try
                            {
                                _ = _onReceive.Function.Call(
                                    DynValue.NewString(text),
                                    DynValue.NewString(ip),
                                    DynValue.NewNumber(port));
                            }
                            catch (InterpreterException ex)
                            {
                                Logger.Error($"[{_mod.Name}] udp onReceive 异常: {ex.DecoratedMessage}");
                            }
                        }
                        finally
                        {
                            _ = Interlocked.Add(ref _pendingBytes, -size);
                        }
                    }, size);
                }
            }
            catch (Exception ex)
            {
                if (!_closed)
                    Post(() =>
                    {
                        if (_onError is null)
                            return;
                        try
                        { _ = _onError.Function.Call(DynValue.NewString(ex.Message)); }
                        catch { }
                    });
            }
        }

        private DynValue Send(CallbackArguments args, bool isBytes)
        {
            if (args.Count < 3 ||
                args[0].Type != DataType.String ||
                args[1].Type != DataType.Number)
                return DynValue.False;

            var host = args[0].String;
            var port = (int)args[1].Number;

            byte[] data;
            if (isBytes)
            {
                if (args[2].Type != DataType.Table)
                    return DynValue.False;
                var arr = args[2].Table;
                data = new byte[arr.Length];
                for (var i = 1; i <= arr.Length; i++)
                {
                    var v = arr.Get(i);
                    if (v.Type == DataType.Number)
                        data[i - 1] = (byte)v.Number;
                }
            }
            else
            {
                if (args[2].Type != DataType.String)
                    return DynValue.False;
                data = Encoding.UTF8.GetBytes(args[2].String);
            }

            try
            {
                _ = _client.Send(data, data.Length, host, port);
                return DynValue.True;
            }
            catch (Exception ex)
            {
                Post(() =>
                {
                    if (_onError is null)
                        return;
                    try
                    { _ = _onError.Function.Call(DynValue.NewString(ex.Message)); }
                    catch { }
                });
                return DynValue.False;
            }
        }

        private void Post(Action action) => LuaMainThread.Queue(action);

        private void PostReceive(Action action, long size)
        {
            // 队列满被丢弃时同步回退字节计数，否则该连接从此永远超限丢包
            if (!LuaMainThread.Queue(action))
                _ = Interlocked.Add(ref _pendingBytes, -size);
        }

        public void Dispose()
        {
            if (_closed)
                return;
            _closed = true;
            try
            { _client.Dispose(); }
            catch { }
            LuaNetworkManager.Unregister(_mod, this);
        }
    }
}
