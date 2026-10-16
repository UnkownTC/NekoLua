using System.Net.WebSockets;
using System.Text;

using LabApi.Features.Console;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Net;

/// <summary>
/// websocket 表（由 Config.EnableNetwork 控制）：
///   local ws = websocket.create(url)
///   ws.onOpen / onMessage / onError / onClose
///   ws:send(text) / close()
///   ws.isOpen / ws.isClosed / ws.state    -- state: "open"/"closed"/...
/// </summary>
public class LuaWebSocketProxy : IRegisterLua
{
    public string Name { get; } = "websocket";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["create"] = DynValue.NewCallback((_, args) =>
        {
            if (!MainPlugin.CacheConfig.EnableNetwork)
                throw new ScriptRuntimeException(
                    "websocket.create: 网络功能未启用（Config.EnableNetwork）");

            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("websocket.create(url)");

            var url = args[0].String;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "ws" && uri.Scheme != "wss"))
                throw new ScriptRuntimeException("websocket.create: 需要 ws:// 或 wss:// URL");

            var ws = new ClientWebSocket();
            var conn = new WsConnection(mod, s, ws);

            if (!LuaNetworkManager.TryRegister(mod, conn))
            {
                ws.Dispose();
                throw new ScriptRuntimeException("websocket.create: 已达连接上限");
            }
#pragma warning disable CS4014
            conn.ConnectAsync(uri);
#pragma warning restore CS4014
            return DynValue.NewTable(conn.GetTable());
        });

        return t;
    }
    internal sealed class WsConnection(LuaModInstance mod, Script script, ClientWebSocket ws) : IDisposable
    {
        // 单条消息重组上限：恶意/被攻陷的服务器可以永续发送 EndOfMessage=false 的分片，
        // 无上限会把 StringBuilder 撑到 OOM（审查 P1-4）
        private const int MaxMessageChars = 8 * 1024 * 1024;

        private readonly LuaModInstance _mod = mod;
        private readonly Script _script = script;
        private readonly ClientWebSocket _ws = ws;

        private DynValue? _onOpen;
        private DynValue? _onMessage;
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
            t["close"] = DynValue.NewCallback((_, _) => { _ = CloseAsync(); return DynValue.Nil; });

            meta["__index"] = DynValue.NewCallback((_, args) =>
            {
                return args.Count < 2 || args[1].Type != DataType.String
                    ? DynValue.Nil
                    : args[1].String switch
                    {
                        "isOpen" => DynValue.NewBoolean(!_closed && _ws.State == WebSocketState.Open),
                        "isClosed" => DynValue.NewBoolean(_closed),
                        "state" => DynValue.NewString(_ws.State.ToString().ToLowerInvariant()),
                        "onOpen" => _onOpen ?? DynValue.Nil,
                        "onMessage" => _onMessage ?? DynValue.Nil,
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
                    case "onOpen":
                        _onOpen = NormFunc(fn);
                        break;
                    case "onMessage":
                        _onMessage = NormFunc(fn);
                        break;
                    case "onError":
                        _onError = NormFunc(fn);
                        break;
                    case "onClose":
                        _onClose = NormFunc(fn);
                        break;
                    default:
                        throw new ScriptRuntimeException($"websocket: 未知字段 '{name}'");
                }

                return DynValue.Nil;
            });

            t.MetaTable = meta;
            _table = t;
            return t;
        }

        private static DynValue? NormFunc(DynValue v)
        {
            return v.Type == DataType.Function ? v : null;
        }

        public async Task ConnectAsync(Uri uri)
        {
            try
            {
                await _ws.ConnectAsync(uri, CancellationToken.None);
                Post(() => Invoke(_onOpen));
                _ = ReceiveLoopAsync();
            }
            catch (Exception ex)
            {
                Post(() => Invoke(_onError, ex.Message));
                Dispose();
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buf = new byte[8192];
            var sb = new StringBuilder();

            try
            {
                while (!_closed && _ws.State == WebSocketState.Open)
                {
                    _ = sb.Clear();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            // onClose 与 Dispose 统一交给 finally 处理，
                            // 避免与 finally 的 Post 叠加造成 onClose 双触发
                            return;
                        }

                        _ = sb.Append(Encoding.UTF8.GetString(buf, 0, result.Count));

                        if (sb.Length > MaxMessageChars)
                        {
                            Post(() => Invoke(_onError, $"消息超过 {MaxMessageChars / 1024 / 1024}MB 上限，连接已关闭"));
                            Dispose();
                            return;
                        }
                    }
                    while (!result.EndOfMessage);

                    var text = sb.ToString();
                    Post(() =>
                    {
                        if (_onMessage is null)
                            return;
                        try
                        { _ = _onMessage.Function.Call(DynValue.NewString(text)); }
                        catch (InterpreterException ex)
                        {
                            Logger.Error($"[{_mod.Name}] ws onMessage 异常: {ex.DecoratedMessage}");
                        }
                    });
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

        private async Task CloseAsync()
        {
            if (_closed)
                return;

            try
            {
                if (_ws.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
            catch { }
            finally { Dispose(); }
        }

        private DynValue Send(CallbackArguments args)
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                return DynValue.False;
            if (_ws.State != WebSocketState.Open)
                return DynValue.False;

            try
            {
                var bytes = Encoding.UTF8.GetBytes(args[0].String);
                _ = _ws.SendAsync(new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text, true, CancellationToken.None);
                return DynValue.True;
            }
            catch (Exception ex)
            {
                Post(() => Invoke(_onError, ex.Message));
                return DynValue.False;
            }
        }

        private void Post(Action action)
        {
            _ = LuaMainThread.Queue(action);
        }

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
                Logger.Error($"[{_mod.Name}] ws 回调异常: {ex.DecoratedMessage}");
            }
        }

        public void Dispose()
        {
            if (_closed)
                return;
            _closed = true;

            try
            { _ws.Dispose(); }
            catch { }
            LuaNetworkManager.Unregister(_mod, this);
        }
    }
}
