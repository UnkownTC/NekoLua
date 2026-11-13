using System.Net;
using System.Text;

using LabApi.Features.Console;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.Net;

/// <summary>
/// ftp 表（由 Config.EnableFtp 控制，默认关闭）：
///
///   ftp.connect(host [, port [, user [, pass]]])   -- 建立会话
///   会话对象 c：
///     c:list([path])            -> table     目录项名列表
///     c:listDetails([path])     -> table     详细列表（含权限、大小）
///     c:download(remote, local) -> bool
///     c:upload(local, remote)   -> bool
///     c:delete(remote)          -> bool
///     c:rename(old, new)        -> bool
///     c:mkdir(remote)           -> bool
///     c:rmdir(remote)           -> bool
///     c:size(remote)            -> number | nil
///     c:close()
///
/// 一次性连接（简写）：
///   ftp.list(host, user, pass, path) -> table
///   ftp.download(host, user, pass, remote, local) -> bool
///   ...
/// </summary>
public class LuaFtpProxy : IRegisterLua
{
    public string Name { get; } = "ftp";

    // FtpWebRequest.Timeout 只覆盖获取响应；数据连接的读写在 ReadWriteTimeout（默认 5 分钟）内
    // 不设限会让主线程长时间停滞，必须显式收紧（审查 P2-22）
    private const int RequestTimeoutMs = 15_000;
    private const int ReadWriteTimeoutMs = 15_000;
    private const int MaxListBytes = 4 * 1024 * 1024;
    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["connect"] = DynValue.NewCallback((_, args) =>
        {
            CheckEnabled();

            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException(
                    "ftp.connect(host [, port [, user [, pass]]])");

            var host = args[0].String;
            var port = args.Count > 1 && args[1].Type == DataType.Number
                ? (int)args[1].Number : 21;
            var user = args.Count > 2 && args[2].Type == DataType.String
                ? args[2].String : "anonymous";
            var pass = args.Count > 3 && args[3].Type == DataType.String
                ? args[3].String : "";

            return DynValue.NewTable(
                BuildSession(s, mod, host, port, user, pass));
        });

        return t;
    }
    private static Table BuildSession(Script script, LuaModInstance mod, string host, int port, string user, string pass)
    {
        var t = new Table(script);
        var baseUrl = $"ftp://{host}:{port}";

        t["list"] = List(script, baseUrl, user, pass, detailed: false);
        t["listDetails"] = List(script, baseUrl, user, pass, detailed: true);

        t["download"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.String)
                throw new ScriptRuntimeException("ftp:download(remote, local)");

            // 审查 P2-3：本地路径必须经 PathGuard，否则 ftp:download 可覆盖服务器任意文件
            var local = LuaPathGuard.Resolve(mod, args[1].String, "ftp:download");

            return DynValue.NewBoolean(
                Download(baseUrl, user, pass, args[0].String, local));
        });

        t["upload"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.String)
                throw new ScriptRuntimeException("ftp:upload(local, remote)");

            // 审查 P2-3：本地路径必须经 PathGuard，否则 ftp:upload 可外传任意文件
            var local = LuaPathGuard.Resolve(mod, args[0].String, "ftp:upload");

            return DynValue.NewBoolean(
                Upload(baseUrl, user, pass, local, args[1].String));
        });

        t["delete"] = Simple(baseUrl, user, pass, WebRequestMethods.Ftp.DeleteFile,
            "ftp:delete(remote)");
        t["mkdir"] = Simple(baseUrl, user, pass, WebRequestMethods.Ftp.MakeDirectory,
            "ftp:mkdir(remote)");
        t["rmdir"] = Simple(baseUrl, user, pass, WebRequestMethods.Ftp.RemoveDirectory,
            "ftp:rmdir(remote)");

        t["rename"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.String)
                throw new ScriptRuntimeException("ftp:rename(old, new)");

            try
            {
                var req = CreateRequest(baseUrl + args[0].String, user, pass);
                req.Method = WebRequestMethods.Ftp.Rename;
                req.RenameTo = args[1].String;

                using var resp = (FtpWebResponse)req.GetResponse();
                return DynValue.NewBoolean(true);
            }
            catch (Exception ex)
            {
                Logger.Warn($"ftp:rename 失败: {ex.Message}");
                return DynValue.False;
            }
        });

        t["size"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("ftp:size(remote)");

            try
            {
                var req = CreateRequest(baseUrl + args[0].String, user, pass);
                req.Method = WebRequestMethods.Ftp.GetFileSize;

                using var resp = (FtpWebResponse)req.GetResponse();
                return DynValue.NewNumber(resp.ContentLength);
            }
            catch
            {
                return DynValue.Nil;
            }
        });

        t["close"] = DynValue.NewCallback((_, _) => DynValue.Nil);

        return t;
    }
    private static DynValue List(Script script, string baseUrl, string user, string pass, bool detailed)
    {
        return DynValue.NewCallback((_, args) =>
        {
            var path = args.Count > 0 && args[0].Type == DataType.String
                ? args[0].String : "/";

            try
            {
                var req = CreateRequest(baseUrl + path, user, pass);
                req.Method = detailed
                    ? WebRequestMethods.Ftp.ListDirectoryDetails
                    : WebRequestMethods.Ftp.ListDirectory;

                using var resp = (FtpWebResponse)req.GetResponse();
                using var reader = new StreamReader(resp.GetResponseStream());

                var text = ReadBounded(reader);
                var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

                var arr = new Table(script);
                for (var i = 0; i < lines.Length; i++)
                    arr[i + 1] = lines[i];

                return DynValue.NewTable(arr);
            }
            catch (Exception ex)
            {
                Logger.Warn($"ftp:list 失败: {ex.Message}");
                return DynValue.NewTable(new Table(script));
            }
        });
    }
    private static DynValue Simple(string baseUrl, string user, string pass, string method, string sig)
    {
        return DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException(sig);

            try
            {
                var req = CreateRequest(baseUrl + args[0].String, user, pass);
                req.Method = method;

                using var resp = (FtpWebResponse)req.GetResponse();
                return DynValue.NewBoolean(true);
            }
            catch (Exception ex)
            {
                Logger.Warn($"ftp 操作失败: {ex.Message}");
                return DynValue.False;
            }
        });
    }
    private static bool Download(string baseUrl, string user, string pass, string remote, string local)
    {
        try
        {
            var req = CreateRequest(baseUrl + remote, user, pass);
            req.Method = WebRequestMethods.Ftp.DownloadFile;

            using var resp = (FtpWebResponse)req.GetResponse();
            using var stream = resp.GetResponseStream();

            var dir = Path.GetDirectoryName(local);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var file = File.Create(local);
            stream.CopyTo(file);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"ftp:download 失败: {ex.Message}");
            return false;
        }
    }
    private static bool Upload(string baseUrl, string user, string pass, string local, string remote)
    {
        try
        {
            if (!File.Exists(local))
                return false;

            var req = CreateRequest(baseUrl + remote, user, pass);
            req.Method = WebRequestMethods.Ftp.UploadFile;

            using var stream = req.GetRequestStream();
            using var file = File.OpenRead(local);
            file.CopyTo(stream);

            using var resp = (FtpWebResponse)req.GetResponse();
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"ftp:upload 失败: {ex.Message}");
            return false;
        }
    }
    private static FtpWebRequest CreateRequest(string url, string user, string pass)
    {
        var req = (FtpWebRequest)WebRequest.Create(url);
        req.Credentials = new NetworkCredential(user, pass);
        req.UseBinary = true;
        req.UsePassive = true;
        req.KeepAlive = false;
        req.Timeout = RequestTimeoutMs;
        req.ReadWriteTimeout = ReadWriteTimeoutMs;
        return req;
    }

    // 目录列表限量读取：恶意 FTP 服务器的超大 listing 会撑爆内存
    private static string ReadBounded(StreamReader reader)
    {
        var sb = new StringBuilder();
        var buf = new char[8192];
        var total = 0;
        int n;

        while ((n = reader.Read(buf, 0, buf.Length)) > 0)
        {
            total += n;
            if (total > MaxListBytes)
                throw new IOException($"目录列表超过 {MaxListBytes / 1024 / 1024}MB 上限");

            _ = sb.Append(buf, 0, n);
        }

        return sb.ToString();
    }
    private static void CheckEnabled()
    {
        if (!MainPlugin.CacheConfig.EnableFtp)
            throw new ScriptRuntimeException(
                "ftp: 未启用（Config.EnableFtp = false）");
    }
}
