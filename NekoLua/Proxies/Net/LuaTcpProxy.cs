using System.Net.Sockets;
using System.Text;

using LabApi.Features.Console;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Net;

/// <summary>
/// tcp 表（由 Config.EnableNetwork 控制）：
///   local c = tcp.create(host, port)      -- 异步连接，立即返回
///   c.onConnect / onReceive / onError / onClose  -- 属性式回调
///   c:send(text) / sendBytes(table)
///   c:close()
///   c.isOpen / c.isClosed
/// </summary>
public class LuaTcpProxy : IRegisterLua
{
    public string Name { get; } = "tcp";

    // 同步 send 在主线程执行：TcpClient 默认 SendTimeout=0（无限），
    // 对端停止接收时会无限期冻结主线程，必须设上限（审查 P2-2）
    private const int SendTimeoutMs = 5000;

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["create"] = DynValue.NewCallback((_, args) =>
        {
            if (!MainPlugin.CacheConfig.EnableNetwork)
                throw new ScriptRuntimeException("tcp.create: 网络功能未启用（Config.EnableNetwork）");

            if (args.Count < 2 ||
                args[0].Type != DataType.String ||
                args[1].Type != DataType.Number)
                throw new ScriptRuntimeException("tcp.create(host, port)");

            var host = args[0].String;
            var port = (int)args[1].Number;

            if (port < 1 || port > 65535)
                throw new ScriptRuntimeException("tcp.create: port 超范围");

            var client = new TcpClient
            {
                SendTimeout = SendTimeoutMs,
            };
            var conn = new TcpConnection(mod, s, client);

            if (!LuaNetworkManager.TryRegister(mod, conn))
            {
                client.Dispose();
                throw new ScriptRuntimeException(
                    $"tcp.create: 已达连接上限 ({MainPlugin.CacheConfig.MaxConnectionsPerMod})");
            }
#pragma warning disable CS4014
            conn.ConnectAsync(host, port);
#pragma warning restore CS4014
            return DynValue.NewTable(conn.GetTable());
        });

        return t;
    }
    internal sealed class TcpConnection(LuaModInstance mod, Script script, TcpClient client) : IDisposable
    {
        private readonly LuaModInstance _mod = mod;
        private readonly Script _script = script;
        private readonly TcpClient _client = client;
        private DynValue? _onConnect;
        private DynValue? _onReceive;
        private DynValue? _onError;
        private DynValue? _onClose;
        private Table? _table;
        private volatile bool _closed;
        public Table GetTable()
        {
            if (_table != null)
                return _table;

            var t = new Table(_script);
            var meta = new Table(_script);

            t["send"] = DynValue.NewCallback((_, a) => Send(a));
            t["sendBytes"] = DynValue.NewCallback((_, a) => SendBytes(a));
            t["close"] = DynValue.NewCallback((_, _) => { Dispose(); return DynValue.Nil; });

            meta["__index"] = DynValue.NewCallback((_, args) =>
            {
                if (args.Count < 2 || args[1].Type != DataType.String)
                    return DynValue.Nil;

                return args[1].String switch
                {
                    "isOpen" => DynValue.NewBoolean(!_closed && _client.Connected),
                    "isClosed" => DynValue.NewBoolean(_closed),
                    "onConnect" => _onConnect ?? DynValue.Nil,
                    "onReceive" => _onReceive ?? DynValue.Nil,
                    "onError" => _onError ?? DynValue.Nil,
                    "onClose" => _onClose ?? DynValue.Nil,
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
                    case "onConnect":
                        _onConnect = NormFunc(fn);
                        break;
                    case "onReceive":
                        _onReceive = NormFunc(fn);
                        break;
                    case "onError":
                        _onError = NormFunc(fn);
                        break;
                    case "onClose":
                        _onClose = NormFunc(fn);
                        break;
                    default:
                        throw new ScriptRuntimeException($"tcp: 未知字段 '{name}'");
                }

                return DynValue.Nil;
            });

            t.MetaTable = meta;
            _table = t;
            return t;
        }

        private static DynValue? NormFunc(DynValue v)
            => v.Type == DataType.Function ? v : null;

        public async Task ConnectAsync(string host, int port)
        {
            try
            {
                await _client.ConnectAsync(host, port);
                Post(() => Invoke(_onConnect));
                _ = ReadLoopAsync();
            }
            catch (Exception ex)
            {
                Post(() => Invoke(_onError, ex.Message));
                Dispose();
            }
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                var stream = _client.GetStream();
                var buf = new byte[8192];

                while (!_closed && _client.Connected)
                {
                    var n = await stream.ReadAsync(buf, 0, buf.Length);
                    if (n <= 0)
                        break;

                    var data = new byte[n];
                    Array.Copy(buf, data, n);

                    var text = Encoding.UTF8.GetString(data);
                    Post(() => Invoke(_onReceive, text));
                }
            }
            catch (Exception ex)
            {
                if (!_closed)
                    Post(() => Invoke(_onError, ex.Message));
            }
            finally
            {
                // Dispose 直接在 IO 线程执行（不触碰 Script，线程安全）：
                // 随回调一起 Post 进主线程队列时，队列满会被整体丢弃导致连接泄漏（审查 P2-21）
                Post(() => Invoke(_onClose));
                Dispose();
            }
        }

        private DynValue Send(CallbackArguments args)
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                return DynValue.False;
            return SendBytesInternal(Encoding.UTF8.GetBytes(args[0].String));
        }

        private DynValue SendBytes(CallbackArguments args)
        {
            if (args.Count < 1 || args[0].Type != DataType.Table)
                return DynValue.False;

            var arr = args[0].Table;
            var data = new byte[arr.Length];
            for (var i = 1; i <= arr.Length; i++)
            {
                var v = arr.Get(i);
                if (v.Type == DataType.Number)
                    data[i - 1] = (byte)v.Number;
            }
            return SendBytesInternal(data);
        }

        private DynValue SendBytesInternal(byte[] data)
        {
            if (_closed || !_client.Connected)
                return DynValue.False;

            try
            {
                var stream = _client.GetStream();
                stream.Write(data, 0, data.Length);
                return DynValue.True;
            }
            catch (Exception ex)
            {
                Post(() => Invoke(_onError, ex.Message));
                return DynValue.False;
            }
        }

        private void Post(Action action) => LuaMainThread.Queue(action);

        private void Invoke(DynValue? fn, params object[] args)
        {
            if (fn is null || fn.Type != DataType.Function)
                return;

            try
            {
                var dynArgs = new DynValue[args.Length];
                for (var i = 0; i < args.Length; i++)
                    dynArgs[i] = args[i] is string s ? DynValue.NewString(s) : DynValue.Nil;

                _ = fn.Function.Call(dynArgs);
            }
            catch (InterpreterException ex)
            {
                Logger.Error($"[{_mod.Name}] tcp 回调异常: {ex.DecoratedMessage}");
            }
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
