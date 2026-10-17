using System.Net.Http;
using System.Text;

using LabApi.Features.Console;

using MoonSharp.Interpreter;
using NekoLua.Api;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Net;

/// <summary>
/// http 表：
///   同步（阻塞主线程）：
///     http.get(url)                 -> (status, body, err)
///     http.post / put / patch / delete(url, body)
///
///   异步（回调在主线程执行）：
///     http.asyncGet(url, callback)
///     http.asyncPost / asyncPut / asyncPatch / asyncDelete(url, body, callback)
///   callback(err, status, body)
/// </summary>
internal class LuaHttpProxy : IRegisterLua
{
    public string Name { get; } = "http";
    private const long MaxResponseBytes = 64 * 1024 * 1024;
    private const int ReadChunkSize = 81920;

    private static HttpClient Client => field ??= new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(MainPlugin.CacheConfig.HttpTimeoutSeconds),
    };
    private static SemaphoreSlim Gate => field ??= new SemaphoreSlim(
        MainPlugin.CacheConfig.HttpMaxConcurrency,
        MainPlugin.CacheConfig.HttpMaxConcurrency);
    private static readonly (string lua, string verb, bool hasBody)[] Methods =
    [
        ("get",    "GET",    false),
        ("post",   "POST",   true),
        ("put",    "PUT",    true),
        ("patch",  "PATCH",  true),
        ("delete", "DELETE", true),
    ];

    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);

        foreach (var (lua, verb, hasBody) in Methods)
        {
            t[lua] = DynValue.NewCallback((_, a) => Sync(verb, a, hasBody));
            t["async" + Cap(lua)] = DynValue.NewCallback((_, a) => Async(mod, verb, a, hasBody));
        }

        return t;
    }

    static DynValue Sync(string method, CallbackArguments args, bool hasBody)
    {
        var uri = ParseUrl(args);
        var body = hasBody ? ParseBody(args, 1) : null;

        try
        {
            using var req = BuildRequest(method, uri, body);

            using var resp = Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)
                .GetAwaiter().GetResult();

            var status = (int)resp.StatusCode;

            var secs = Math.Max(1, MainPlugin.CacheConfig.HttpTimeoutSeconds);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(secs));
            var text = ReadBounded(resp.Content, cts.Token).GetAwaiter().GetResult();

            return DynValue.NewTuple(
                DynValue.NewNumber(status),
                DynValue.NewString(text),
                DynValue.Nil);
        }
        catch (Exception ex)
        {
            return DynValue.NewTuple(
                DynValue.Nil,
                DynValue.Nil,
                DynValue.NewString(ex.Message));
        }
    }
    private static async Task<string> ReadBounded(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength is > MaxResponseBytes)
            throw new IOException($"响应体 {content.Headers.ContentLength} 字节超过上限 {MaxResponseBytes / 1024 / 1024}MB");

        using var stream = await content.ReadAsStreamAsync();
        using var output = new MemoryStream();
        var buf = new byte[ReadChunkSize];
        long total = 0;
        int n;

        while ((n = await stream.ReadAsync(buf, 0, buf.Length, ct)) > 0)
        {
            total += n;
            if (total > MaxResponseBytes)
                throw new IOException($"响应体超过上限 {MaxResponseBytes / 1024 / 1024}MB");

            output.Write(buf, 0, n);
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    static DynValue Async(LuaModInstance mod, string method, CallbackArguments args, bool hasBody)
    {
        var uri = ParseUrl(args);

        var bodyIdx = hasBody ? 1 : -1;
        var cbIdx = hasBody ? 2 : 1;

        var body = hasBody ? ParseBody(args, bodyIdx) : null;

        if (args.Count <= cbIdx || args[cbIdx].Type != DataType.Function)
            throw new ScriptRuntimeException(
                $"http.async{Cap(method)}(url{(hasBody ? ", body" : "")}, callback)");

        var callback = args[cbIdx];
        var script = mod.Script;
        var name = mod.Name;

        _ = Task.Run(() => RunAsync(script, name, callback, method, uri, body));
        return DynValue.Nil;
    }

    static async Task RunAsync(
        Script _, string modName, DynValue callback,
        string method, Uri uri, string? body)
    {
        try
        {
            var secs = Math.Max(1, MainPlugin.CacheConfig.HttpTimeoutSeconds);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(secs));

            await Gate.WaitAsync(cts.Token);
            try
            {
                using var req = BuildRequest(method, uri, body);
                using var resp = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                var status = (int)resp.StatusCode;
                var text = await ReadBounded(resp.Content, cts.Token);

                Post(modName, callback,
                    DynValue.Nil,
                    DynValue.NewNumber(status),
                    DynValue.NewString(text));
            }
            finally
            {
                Gate.Release();
            }
        }
        catch (TaskCanceledException)
        {
            Post(modName, callback,
                DynValue.NewString("请求超时"),
                DynValue.Nil, DynValue.Nil);
        }
        catch (Exception ex)
        {
            Post(modName, callback,
                DynValue.NewString(ex.Message),
                DynValue.Nil, DynValue.Nil);
        }
    }

    static void Post(string modName, DynValue callback, params DynValue[] args)
    {
        var ok = LuaMainThread.Queue(() =>
        {
            try
            {
                _ = callback.Function.Call(args);
            }
            catch (InterpreterException ex)
            {
                Logger.Error($"[{modName}] http 回调异常: {ex.DecoratedMessage}");
            }
        });

        if (!ok)
            Logger.Warn($"[{modName}] http 回调队列已满，本次回调被丢弃");
    }

    static Uri ParseUrl(CallbackArguments args)
    {
        return args.Count < 1 || args[0].Type != DataType.String
            ? throw new ScriptRuntimeException("http: url 需要字符串")
            : !Uri.TryCreate(args[0].String, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https")
            ? throw new ScriptRuntimeException("http: 只支持 http/https URL")
            : uri;
    }

    static string? ParseBody(CallbackArguments args, int index)
    {
        if (args.Count <= index)
            return null;

        var v = args[index];
        return v.Type is DataType.Nil or DataType.Void
            ? null
            : v.Type == DataType.String ? v.String : v.ToPrintString();
    }

    static HttpRequestMessage BuildRequest(string method, Uri uri, string? body)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), uri);
        if (body != null)
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return req;
    }
    private static readonly Dictionary<string, string> _capCache = new(StringComparer.Ordinal)
    {
        ["GET"] = "Get",
        ["POST"] = "Post",
        ["PUT"] = "Put",
        ["PATCH"] = "Patch",
        ["DELETE"] = "Delete",
    };
    static string Cap(string method)
        => _capCache.TryGetValue(method, out var c) ? c : method;
}
