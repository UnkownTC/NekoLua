using System.Diagnostics;
using System.Text;

using LabApi.Features.Console;

using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies;

/// <summary>
/// process 表（由 Config.EnableProcess 控制，默认关闭）：
///
///   process.run(cmd[, args[, callback]])  -> pid
///     异步启动，callback(exit, stdout, stderr) 在主线程执行
///
///   process.runSync(cmd[, args])          -> (exit, stdout, stderr)
///     同步阻塞，会卡住主线程，慎用
///
///   process.start(cmd[, args])            -> pid
///     后台启动，不捕获输出
///
///   process.kill(pid)                     -> bool
///     受 Config.ProcessKillBlacklist 和
///     Config.ProcessKillSelfForbidden 保护
///
///   process.exists(pid)                   -> bool
///
/// args 是字符串数组（table），不是 shell 命令串
/// 白名单：Config.ProcessWhitelist（空=允许所有）
/// </summary>
public class LuaProcessProxy : IRegisterLua
{
    public string Name { get; } = "process";

    // runSync 在主线程阻塞：无超时的话子进程跑多久主线程就卡多久，必须给上限（审查 P2-1）
    private const int RunSyncTimeoutMs = 30_000;

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["run"] = DynValue.NewCallback((_, args) =>
        {
            var (cmd, argArr, callback) = ParseRun(args, "process.run(cmd[, args[, callback]])");
            CheckAllowed(cmd);

            try
            {
                var psi = BuildPsi(cmd, argArr, redirect: callback is not null);
                var proc = Process.Start(psi)
                    ?? throw new Exception("启动进程失败");

                if (callback is not null)
#pragma warning disable CS4014
                    CaptureAsync(mod, proc, callback);
#pragma warning restore CS4014

                return DynValue.NewNumber(proc.Id);
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"process.run: {ex.Message}");
            }
        });

        t["runSync"] = DynValue.NewCallback((_, args) =>
        {
            var (cmd, argArr, _) = ParseRun(args, "process.runSync(cmd[, args])");
            CheckAllowed(cmd);

            try
            {
                var psi = BuildPsi(cmd, argArr, redirect: true);

                using var proc = Process.Start(psi)
                    ?? throw new Exception("启动进程失败");

                // 两条管道必须并发排空：顺序 ReadToEnd 会在 stderr 写满管道缓冲
                // 而子进程未退出时互相等待，形成不可恢复的主线程死锁（审查 P2-1）
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();

                if (!proc.WaitForExit(RunSyncTimeoutMs))
                {
                    try
                    { proc.Kill(); }
                    catch (Exception ex)
                    {
                        LuaDiagnostics.WarnOnce("process.runSync: 超时后终止子进程失败，进程可能仍在运行", ex);
                    }

                    // 排空两个管道任务，避免未观察 Task 异常；lambda 首参占用了 _，此处不能用弃元
                    try
                    { stdoutTask.Wait(1000); }
                    catch (Exception ex)
                    {
                        // 仅为观察异常、避免未观察 Task 异常，此处记录即可
                        LuaDiagnostics.WarnOnce("process.runSync: 排空 stdout 管道任务时出错", ex);
                    }

                    try
                    { stderrTask.Wait(1000); }
                    catch (Exception ex)
                    {
                        LuaDiagnostics.WarnOnce("process.runSync: 排空 stderr 管道任务时出错", ex);
                    }

                    throw new ScriptRuntimeException(
                        $"process.runSync: 子进程超过 {RunSyncTimeoutMs}ms 未退出，已强制终止");
                }

                var stdout = stdoutTask.GetAwaiter().GetResult();
                var stderr = stderrTask.GetAwaiter().GetResult();

                return DynValue.NewTuple(
                    DynValue.NewNumber(proc.ExitCode),
                    DynValue.NewString(stdout),
                    DynValue.NewString(stderr));
            }
            catch (ScriptRuntimeException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"process.runSync: {ex.Message}");
            }
        });

        t["start"] = DynValue.NewCallback((_, args) =>
        {
            var (cmd, argArr, _) = ParseRun(args, "process.start(cmd[, args])");
            CheckAllowed(cmd);

            try
            {
                var psi = BuildPsi(cmd, argArr, redirect: false);
                var proc = Process.Start(psi)
                    ?? throw new Exception("启动进程失败");
                return DynValue.NewNumber(proc.Id);
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"process.start: {ex.Message}");
            }
        });

        t["kill"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("process.kill(pid)");

            var pid = (int)args[0].Number;

            try
            {
                var proc = Process.GetProcessById(pid);
                if (MainPlugin.CacheConfig.ProcessKillSelfForbidden)
                {
                    var selfId = Process.GetCurrentProcess().Id;
                    if (pid == selfId)
                        throw new ScriptRuntimeException(
                            "process.kill: 禁止杀死当前进程（服务端自身）");
                }
                var blacklist = MainPlugin.CacheConfig.ProcessKillBlacklist;
                if (blacklist is { Length: > 0 })
                {
                    var name = proc.ProcessName;
                    foreach (var blocked in blacklist)
                    {
                        if (string.Equals(blocked, name, StringComparison.OrdinalIgnoreCase))
                            throw new ScriptRuntimeException(
                                $"process.kill: 进程 '{name}' 在黑名单中，禁止终止");
                    }
                }

                proc.Kill();
                return DynValue.True;
            }
            catch (ScriptRuntimeException)
            {
                throw;
            }
            catch
            {
                return DynValue.False;
            }
        });

        t["exists"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("process.exists(pid)");

            try
            {
                Process.GetProcessById((int)args[0].Number);
                return DynValue.True;
            }
            catch
            {
                return DynValue.False;
            }
        });

        return t;
    }
    private static void CheckAllowed(string cmd)
    {
        var cfg = MainPlugin.CacheConfig;

        if (!cfg.EnableProcess)
            throw new ScriptRuntimeException(
                "process: 未启用（Config.EnableProcess = false）");

        if (cfg.ProcessWhitelist is not { Length: > 0 })
            return;

        // 审查 P2-12：白名单项含路径分隔符时按规范化全路径比较（强模式，不可被同名 exe 绕过）；
        // 裸文件名项仍按名字比较（向后兼容），但配置时建议优先写全路径。
        var name = Path.GetFileNameWithoutExtension(cmd);
        var full = TryGetFullPath(cmd);

        foreach (var allowed in cfg.ProcessWhitelist)
        {
            if (string.IsNullOrWhiteSpace(allowed))
                continue;

            if (IsPathEntry(allowed))
            {
                var allowedFull = TryGetFullPath(allowed);

                if (allowedFull is not null && full is not null &&
                    string.Equals(allowedFull, full, StringComparison.OrdinalIgnoreCase))
                    return;

                continue;
            }

            if (string.Equals(allowed, cmd, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(allowed, name, StringComparison.OrdinalIgnoreCase))
                return;
        }

        throw new ScriptRuntimeException(
            $"process: 命令 '{cmd}' 不在白名单中");
    }

    private static bool IsPathEntry(string entry)
        => entry.IndexOf(Path.DirectorySeparatorChar) >= 0
        || entry.IndexOf(Path.AltDirectorySeparatorChar) >= 0;

    private static string? TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// .NET Framework 4.8.1 没有 ArgumentList，用 Arguments 字符串拼接。
    /// </summary>
    private static ProcessStartInfo BuildPsi(string cmd, string[] args, bool redirect)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cmd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
        };

        if (args.Length > 0)
            psi.Arguments = JoinArguments(args);

        return psi;
    }
    private static string JoinArguments(string[] args)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < args.Length; i++)
        {
            if (i > 0)
                _ = sb.Append(' ');

            var arg = args[i];

            if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0)
            {
                _ = sb.Append(arg);
                continue;
            }
            _ = sb.Append('"');

            var backslashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\')
                {
                    backslashes++;
                }
                else if (c == '"')
                {
                    // 反斜杠数 + 1，再输出 \" 
                    _ = sb.Append('\\', (backslashes * 2) + 1);
                    _ = sb.Append('"');
                    backslashes = 0;
                }
                else
                {
                    _ = sb.Append('\\', backslashes);
                    backslashes = 0;
                    _ = sb.Append(c);
                }
            }

            // 尾部反斜杠要翻倍
            _ = sb.Append('\\', backslashes * 2);
            _ = sb.Append('"');
        }

        return sb.ToString();
    }

    private static (string cmd, string[] args, DynValue? cb)
        ParseRun(CallbackArguments args, string sig)
    {
        if (args.Count < 1 || args[0].Type != DataType.String)
            throw new ScriptRuntimeException(sig);

        var cmd = args[0].String;

        string[] cmdArgs = [];
        if (args.Count > 1 && args[1].Type == DataType.Table)
        {
            var arr = args[1].Table;
            var list = new List<string>(arr.Length);
            for (var i = 1; i <= arr.Length; i++)
            {
                var v = arr.Get(i);
                if (v.Type == DataType.String)
                    list.Add(v.String);
            }
            cmdArgs = [.. list];
        }

        DynValue? cb = null;
        for (var i = 1; i < args.Count; i++)
        {
            if (args[i].Type == DataType.Function)
            { cb = args[i]; break; }
        }

        return (cmd, cmdArgs, cb);
    }
    private static async Task CaptureAsync(LuaModInstance mod, Process proc, DynValue cb)
    {
        try
        {
            var stdoutTask = Task.Run(() => proc.StandardOutput.ReadToEnd());
            var stderrTask = Task.Run(() => proc.StandardError.ReadToEnd());
            var exitTask = Task.Run(() => { proc.WaitForExit(); return proc.ExitCode; });

            await Task.WhenAll(stdoutTask, stderrTask, exitTask);

            var stdout = stdoutTask.Result;
            var stderr = stderrTask.Result;
            var exit = exitTask.Result;

            _ = LuaMainThread.Queue(() =>
            {
                try
                {
                    _ = cb.Function.Call(
                        DynValue.NewNumber(exit),
                        DynValue.NewString(stdout),
                        DynValue.NewString(stderr));
                }
                catch (InterpreterException ex)
                {
                    Logger.Error($"[{mod.Name}] process 回调异常: {ex.DecoratedMessage}");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"[{mod.Name}] process 捕获异常: {ex.Message}");
        }
        finally
        {
            proc.Dispose();
        }
    }
}
