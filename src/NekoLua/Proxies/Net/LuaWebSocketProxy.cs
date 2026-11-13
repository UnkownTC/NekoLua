using System.Collections.Concurrent;
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
///
/// 注意：`ws:send()` 返回 `true` 仅表示**已进入发送队列**（不是"已送达对端"）。
/// 真正发送失败会走 `ws.onError`。这样设计是因为 `ClientWebSocket` 不允许并发
/// `SendAsync`，而 Lua 侧是同步调用、不能阻塞服务端主线程等待发送完成。
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

            // 审查 P2-18：阻断脚本访问本机/内网地址（SSRF）
            LuaNetworkGuard.EnsureUriAllowed(uri, "websocket.create");

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

        // 待发队列字节上限：脚本狂发时无上限会把队列撑爆内存（与 udp 的 P2-19 同口径）
        private const int MaxPendingSendBytes = 4 * 1024 * 1024;

        private readonly LuaModInstance _mod = mod;
        private readonly Script _script = script;
        private readonly ClientWebSocket _ws = ws;

        // ClientWebSocket 不允许并发 SendAsync（重入抛 "There is already one outstanding
        // 'SendAsync' call"），而 Lua 的 `ws:send()` 是同步调用、必须立即返回且不能阻塞
        // 服务端主线程，故改为「有界队列 + 单泵串行发送」：
        //   - 消除并发 SendAsync 重入异常；
        //   - 消除原实现 fire-and-forget（`_ = _ws.SendAsync(...)` 后无条件 return true）
        //     导致发送异常既不被 catch 捕获、又向脚本假报成功的问题（审查 P2-20）。
        private readonly ConcurrentQueue<byte[]> _sendQueue = new();
        private readonly SemaphoreSlim _sendSignal = new(0);
        private int _pendingSendBytes;
        private int _pumpStarted;

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
            catch (Exception ex)
            {
                // 关闭握手失败不影响本地资源回收，finally 仍会 Dispose
                LuaDiagnostics.WarnOnce($"[{_mod.Name}] ws 关闭握手失败", ex);
            }
            finally { Dispose(); }
        }

        private DynValue Send(CallbackArguments args)
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                return DynValue.False;
            if (_closed || _ws.State != WebSocketState.Open)
                return DynValue.False;

            var bytes = Encoding.UTF8.GetBytes(args[0].String);

            // 有界入队：超限直接拒绝并回一次 onError，避免脚本狂发撑爆内存。
            // 先加后判，超限时立即回退，保证计数与队列一致。
            if (Interlocked.Add(ref _pendingSendBytes, bytes.Length) > MaxPendingSendBytes)
            {
                _ = Interlocked.Add(ref _pendingSendBytes, -bytes.Length);
                Post(() => Invoke(_onError,
                    $"发送队列超过 {MaxPendingSendBytes / 1024 / 1024}MB 上限，本条消息已丢弃"));
                return DynValue.False;
            }

            _sendQueue.Enqueue(bytes);
            StartSendPump();
            _ = _sendSignal.Release();

            // 语义：true = 已受理入队（非"已送达"）；真正发送失败走 onError
            return DynValue.True;
        }

        /// <summary>
        /// 惰性启动发送泵。用 CAS 保证同一连接只有一个泵在跑；
        /// 泵随连接存活（队列空时等待信号量），不使用「队列空即退出」的写法，
        /// 避免「泵判空 → send() 入队 → 泵退出」造成该条消息永远不被发送。
        /// </summary>
        private void StartSendPump()
        {
            if (Interlocked.CompareExchange(ref _pumpStarted, 1, 0) != 0)
                return;
#pragma warning disable CS4014
            Task.Run(PumpSendAsync);
#pragma warning restore CS4014
        }

        private async Task PumpSendAsync()
        {
            while (true)
            {
                try
                {
                    await _sendSignal.WaitAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (_closed)
                    return;

                while (_sendQueue.TryDequeue(out var bytes))
                {
                    try
                    {
                        await _ws.SendAsync(new ArraySegment<byte>(bytes),
                            WebSocketMessageType.Text, true, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _ = Interlocked.Add(ref _pendingSendBytes, -bytes.Length);
                        Post(() => Invoke(_onError, ex.Message));
                        Dispose();          // 发送已失败，套接字状态不可信，按关闭处理
                        return;
                    }

                    _ = Interlocked.Add(ref _pendingSendBytes, -bytes.Length);
                }
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

            // 丢弃未发消息并归零字节计数，否则残留计数会让该连接永久超限
            while (_sendQueue.TryDequeue(out _))
            {
            }
            _ = Interlocked.Exchange(ref _pendingSendBytes, 0);

            // 唤醒挂起的发送泵让它看到 _closed 后退出。
            // SemaphoreSlim 未访问 AvailableWaitHandle 时不持有 OS 句柄，故不显式 Dispose
            // （避免与并发 WaitAsync 竞争抛 ObjectDisposedException）。
            try
            { _ = _sendSignal.Release(); }
            catch (SemaphoreFullException ex)
            {
                // 信号量已满说明发送泵已在等待/运行，本次唤醒可省略。
                // 概率极低（需连续 Release 而泵未 Wait），仍按 §7.1 记录而非空吞。
                LuaDiagnostics.WarnOnce($"[{_mod.Name}] ws 发送信号量已满，跳过唤醒", ex);
            }

            try
            { _ws.Dispose(); }
            catch (Exception ex)
            {
                LuaDiagnostics.WarnOnce($"[{_mod.Name}] ws 底层连接释放失败", ex);
            }
            LuaNetworkManager.Unregister(_mod, this);
        }
    }
}
