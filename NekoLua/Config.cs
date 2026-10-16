using System.ComponentModel;

namespace NekoLua;

public class Config
{
    [Description("HTTP 请求超时（单位为秒）")]
    public int HttpTimeoutSeconds { get; set; } = 15;

    [Description("HTTP 异步请求并发上限")]
    public int HttpMaxConcurrency { get; set; } = 16;


    [Description("启动时把内置 Lua 库解压到 dependencies/global/ 目录")]
    public bool ExtractEmbeddedLua { get; set; } = true;
    [Description("解压内置 Lua 库时是否覆盖已存在的同名文件（false = 只解压缺失的）")]
    public bool OverwriteEmbeddedLua { get; set; } = false;

    [Description("主线程任务队列上限")]
    public int MainThreadQueueLimit { get; set; } = 4096;

    [Description("每帧最多处理的主线程任务数")]
    public int MainThreadTasksPerTick { get; set; } = 64;

    [Description("禁止访问的路径前缀（不区分大小写）")]
    public string[] BlockedPaths { get; set; } = BuildDefaultBlockedPaths();

    [Description("允许更改环境变量")]
    public bool EnvWriteEnabled { get; set; } = false;

    [Description("timer连续捕获几次异常后自动移除（<=0 表示从不移除）")]
    public int TimerMaxConsecutiveErrors { get; set; } = 5;

    [Description("启用复杂网络功能（tcp/udp/websocket）")]
    public bool EnableNetwork { get; set; } = true;

    [Description("每个MOD最多同时打开的连接数")]
    public int MaxConnectionsPerMod { get; set; } = 16;
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
            catch { }
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
