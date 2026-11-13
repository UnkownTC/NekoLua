using System.ComponentModel;

using NekoLua.Dependencies;

namespace NekoLua;

public class Config
{
    [Description("启动时把内置 Lua 库解压到 dependencies/global/ 目录")]
    public bool ExtractEmbeddedLua { get; set; } = true;
    [Description("解压内置 Lua 库时是否覆盖已存在的同名文件（false = 只解压缺失的）")]
    public bool OverwriteEmbeddedLua { get; set; } = false;

    [Description("主线程任务队列上限")]
    public int MainThreadQueueLimit { get; set; } = 4096;

    [Description("每帧最多处理的主线程任务数")]
    public int MainThreadTasksPerTick { get; set; } = 64;

    [Description("HTTP请求超时（秒）")]
    public int HttpTimeoutSeconds { get; set; } = 15;

    [Description("HTTP异步请求并发上限")]
    public int HttpMaxConcurrency { get; set; } = 16;
    [Description("启用 FTP 客户端（ftp.*）")]
    public bool EnableFtp { get; set; } = false;

    [Description("禁止访问的路径前缀（不区分大小写）")]
    public string[] BlockedPaths { get; set; } = BuildDefaultBlockedPaths();

    [Description("额外放行的路径（文件/目录 API 默认只允许访问脚本自己的 mod 目录，此处用于显式放行共享数据目录）")]
    public string[] AllowedPaths { get; set; } = [];

    [Description("允许更改环境变量")]
    public bool EnvWriteEnabled { get; set; } = false;

    [Description("timer连续捕获几次异常后自动移除（<=0 表示从不移除）")]
    public int TimerMaxConsecutiveErrors { get; set; } = 5;

    [Description("启用复杂网络功能（tcp/udp/websocket）")]
    public bool EnableNetwork { get; set; } = true;

    [Description("禁止脚本访问本机/内网地址（环回、RFC1918 私网、链路本地、云元数据），防 SSRF；如需脚本访问内网服务请置 false")]
    public bool BlockPrivateNetworkTargets { get; set; } = true;

    [Description("每个MOD最多同时打开的连接数")]
    public int MaxConnectionsPerMod { get; set; } = 16;

    [Description("启用进程执行功能")]
    public bool EnableProcess { get; set; } = false;

    [Description("允许执行的程序白名单（空 = 允许所有）")]
    public string[] ProcessWhitelist { get; set; } = [];

    [Description("禁止 process.kill 的目标进程名（不含扩展名，不区分大小写）")]
    public string[] ProcessKillBlacklist { get; set; } =
    [
        // Windows
        "system", "idle", "csrss", "wininit", "winlogon",
        "services", "lsass", "smss", "svchost", "conhost",

        // Linux
        "init", "systemd", "kthreadd",
    ];
    [Description("禁止 process.kill 杀死当前进程（自身）")]
    public bool ProcessKillSelfForbidden { get; set; } = true;


    [Description("启用 Win32 原生调用（win32.*），高风险功能")]
    public bool EnableWin32 { get; set; } = false;

    [Description("Win32 允许加载的 dll 白名单（空 = 允许所有）")]
    public string[] Win32DllWhitelist { get; set; } =
    [
        "kernel32.dll",
        "user32.dll",
        "shell32.dll",
        "gdi32.dll",
        "advapi32.dll",
        "ntdll.dll",
    ];

    [Description("Win32 允许调用的函数白名单（空 = 允许所有）")]
    public string[] Win32FunctionWhitelist { get; set; } = [];

    [Description("Win32 禁止调用的函数黑名单（优先级高于白名单，即使白名单为空也生效）")]
    public string[] Win32FunctionBlacklist { get; set; } =
    [
        // 进程创建 / 终止
        "WinExec", "CreateProcessA", "CreateProcessW", "ShellExecuteA", "ShellExecuteW",
        "ShellExecuteExA", "ShellExecuteExW", "TerminateProcess", "NtTerminateProcess",
        // 跨进程内存读写
        "OpenProcess", "NtOpenProcess", "ReadProcessMemory", "NtReadVirtualMemory",
        "WriteProcessMemory", "NtWriteVirtualMemory", "VirtualAllocEx", "VirtualProtectEx",
        "CreateRemoteThread", "NtCreateThreadEx", "QueueUserAPC",
        // 动态库加载
        "LoadLibraryA", "LoadLibraryW", "LoadLibraryExA", "LoadLibraryExW",
        "LdrLoadDll", "GetProcAddress",
    ];

    [Description("允许 Win32 调用把 {type=\"ptr\", value=...} 作为参数传入（高风险，默认关闭）")]
    public bool Win32AllowPointerArgs { get; set; } = false;

    [Description("Win32 调用返回 IntPtr 时是否转成 intptr 代理表（false = 返回数字）")]
    public bool Win32IntPtrAsTable { get; set; } = true;


    [Description("启用NVorbis")]
    public bool EnableNvorbis { get; set; } = true;
    [Description("NVorbis一次性解码的样本上限（0 = 不限，不建议：无上限解码会冻结主线程）")]
    public long NvorbisMaxSamples { get; set; } = 10_000_000;

    [Description("允许 Lua 脚本通过 server.runCommand 执行服务器控制台命令（高风险，默认关闭）")]
    public bool AllowScriptRunCommand { get; set; } = false;

    [Description("允许 server.runCommand 执行的命令前缀白名单（空 = 不限前缀；仅在 AllowScriptRunCommand=true 时生效）")]
    public string[] RunCommandPrefixWhitelist { get; set; } = [];

    [Description("禁止 Lua 脚本调用的全服级 API 标签（空 = 不限制）。" +
                 "可用标签：warhead / round / server / whitelist / reservedSlots / permissions / " +
                 "broadcast / cassie / decontamination / map / hazard / pocket / scp914；" +
                 "扩展程序集可登记额外标签（如 astra / astra.role / astra.team / astra.wave）")]
    public string[] DeniedGlobalApis { get; set; } = [];
    private static string[] BuildDefaultBlockedPaths()
    {
        var list = new List<string>();

        void Add(Environment.SpecialFolder folder)
        {
            try
            {
                var p = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(p))
                    list.Add(p);
            }
            catch (Exception ex)
            {
                LuaDiagnostics.Warn($"Config: 读取系统目录 {folder} 失败，该目录不会纳入默认拒绝表", ex);
            }
        }

        Add(Environment.SpecialFolder.Windows);
        Add(Environment.SpecialFolder.System);
        Add(Environment.SpecialFolder.SystemX86);
        Add(Environment.SpecialFolder.ProgramFiles);
        Add(Environment.SpecialFolder.ProgramFilesX86);
        Add(Environment.SpecialFolder.CommonApplicationData);

        list.Add("/etc");
        list.Add("/boot");
        list.Add("/sys");
        list.Add("/proc");
        list.Add("/root");

        return [.. list.Distinct(StringComparer.OrdinalIgnoreCase)];
    }
}
