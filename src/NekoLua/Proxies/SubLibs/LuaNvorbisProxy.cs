using MoonSharp.Interpreter;

using NekoLua.Dependencies;
using NekoLua.Loader;
using NekoLua.Registers;

using NVorbis;

namespace NekoLua.Proxies.SubLibs;

/// <summary>
/// nvorbis 表（由 Config.EnableNvorbis 控制）—— OGG 音频读取：
///
///   元数据：
///     nvorbis.info(path)                  -> table
///       { sampleRate, channels, totalSamples, duration }
///
///   一次性解码（浮点样本，交错 [-1, 1]）：
///     nvorbis.read(path)                  -> table   全部
///     nvorbis.read(path, maxSamples)      -> table   前 N 个
///       上限由 Config.NvorbisMaxSamples 控制（默认 5000 万）
///
///   流式回调（超大文件推荐，避免一次持有）：
///     nvorbis.eachChunk(path, chunkSize, callback)
///       callback(samples, index)          index 从 1 开始计数
///       返回是否被中断（callback 返回 false 会提前终止）
///
///   流式解码器：
///     local s = nvorbis.open(path)
///       s.sampleRate / channels / totalSamples / duration
///       s:read(count)                     -> table
///       s:seek(sampleIndex)               -> bool
///       s:pos()                           -> number
///       s:close()
/// </summary>
public class LuaNvorbisProxy : IRegisterLua
{
    public string Name { get; } = "nvorbis";

    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);

        t["info"] = DynValue.NewCallback((_, a) => Info(mod, a));
        t["read"] = DynValue.NewCallback((_, a) => Read(mod, a));
        t["eachChunk"] = DynValue.NewCallback((_, a) => EachChunk(mod, a));
        t["open"] = DynValue.NewCallback((_, a) => Open(mod, a));

        return t;
    }

    // ================================================================
    // 元数据
    // ================================================================

    private static DynValue Info(LuaModInstance mod, CallbackArguments args)
    {
        CheckEnabled();
        if (args.Count < 1 || args[0].Type != DataType.String)
            throw new ScriptRuntimeException("nvorbis.info(path)");

        var path = Resolve(mod, args[0].String);

        try
        {
            using var reader = new VorbisReader(path);
            return DynValue.NewTable(new Table(mod.Script)
            {
                ["sampleRate"] = reader.SampleRate,
                ["channels"] = reader.Channels,
                ["totalSamples"] = reader.TotalSamples,
                ["duration"] = reader.TotalTime.TotalSeconds,
            });
        }
        catch (Exception ex)
        {
            throw new ScriptRuntimeException($"nvorbis.info: {ex.Message}");
        }
    }

    // ================================================================
    // 一次性读完
    // ================================================================

    private static DynValue Read(LuaModInstance mod, CallbackArguments args)
    {
        CheckEnabled();
        if (args.Count < 1 || args[0].Type != DataType.String)
            throw new ScriptRuntimeException("nvorbis.read(path[, maxSamples])");

        var path = Resolve(mod, args[0].String);
        var max = args.Count > 1 && args[1].Type == DataType.Number
            ? (long)args[1].Number
            : -1;

        try
        {
            using var reader = new VorbisReader(path);
            var total = reader.TotalSamples;

            if (max < 0 || max > total)
                max = total;

            if (max > int.MaxValue)
                throw new ScriptRuntimeException("nvorbis.read: maxSamples 过大");

            var limit = MainPlugin.CacheConfig.NvorbisMaxSamples;
            if (limit > 0 && max > limit)
                throw new ScriptRuntimeException(
                    $"nvorbis.read: 请求 {max} 样本超过上限 {limit}（可在 Config 调整 NvorbisMaxSamples）");

            var buf = new float[(int)max];
            var read = reader.ReadSamples(buf, 0, (int)max);

            var arr = new Table(mod.Script);
            for (var i = 0; i < read; i++)
                arr[i + 1] = buf[i];

            return DynValue.NewTable(arr);
        }
        catch (ScriptRuntimeException) { throw; }
        catch (Exception ex)
        {
            throw new ScriptRuntimeException($"nvorbis.read: {ex.Message}");
        }
    }
    private static DynValue EachChunk(LuaModInstance mod, CallbackArguments args)
    {
        CheckEnabled();
        if (args.Count < 3 ||
            args[0].Type != DataType.String ||
            args[1].Type != DataType.Number ||
            args[2].Type != DataType.Function)
            throw new ScriptRuntimeException("nvorbis.eachChunk(path, chunkSize, callback)");

        var path = Resolve(mod, args[0].String);
        var chunk = (int)args[1].Number;
        if (chunk < 1 || chunk > 1_000_000)
            throw new ScriptRuntimeException("nvorbis.eachChunk: chunkSize 应在 1~1000000");

        var callback = args[2];

        try
        {
            // 累计解码样本数同样受 NvorbisMaxSamples 约束：
            // eachChunk 的分块是瞬态的，不封顶的话整文件解码会冻结主线程（审查 P2-9）
            var limit = MainPlugin.CacheConfig.NvorbisMaxSamples;
            var decoded = 0L;

            using var reader = new VorbisReader(path);
            var buf = new float[chunk];
            var idx = 0;
            var interrupted = false;

            while (true)
            {
                var read = reader.ReadSamples(buf, 0, chunk);
                if (read <= 0)
                    break;

                decoded += read;
                if (limit > 0 && decoded > limit)
                    throw new ScriptRuntimeException(
                        $"nvorbis.eachChunk: 累计解码 {decoded} 样本超过上限 {limit}（Config.NvorbisMaxSamples）");

                var tbl = new Table(mod.Script);
                for (var i = 0; i < read; i++)
                    tbl[i + 1] = buf[i];

                idx++;

                DynValue r;
                try
                {
                    r = callback.Function.Call(
                        DynValue.NewTable(tbl),
                        DynValue.NewNumber(idx));
                }
                catch (InterpreterException ex)
                {
                    throw new ScriptRuntimeException(
                        $"nvorbis.eachChunk: 回调异常: {ex.DecoratedMessage}");
                }

                // callback 返回 false 提前终止
                if (r.Type == DataType.Boolean && !r.Boolean)
                {
                    interrupted = true;
                    break;
                }
            }

            return DynValue.NewBoolean(!interrupted);
        }
        catch (ScriptRuntimeException) { throw; }
        catch (Exception ex)
        {
            throw new ScriptRuntimeException($"nvorbis.eachChunk: {ex.Message}");
        }
    }

    // ================================================================
    // 流式读取
    // ================================================================

    private static DynValue Open(LuaModInstance mod, CallbackArguments args)
    {
        CheckEnabled();
        if (args.Count < 1 || args[0].Type != DataType.String)
            throw new ScriptRuntimeException("nvorbis.open(path)");

        var path = Resolve(mod, args[0].String);

        try
        {
            return DynValue.NewTable(BuildStream(mod.Script, new VorbisReader(path)));
        }
        catch (Exception ex)
        {
            throw new ScriptRuntimeException($"nvorbis.open: {ex.Message}");
        }
    }

    private static Table BuildStream(Script s, VorbisReader reader)
    {
        var t = new Table(s);
        var closed = false;

        t["sampleRate"] = reader.SampleRate;
        t["channels"] = reader.Channels;
        t["totalSamples"] = reader.TotalSamples;
        t["duration"] = reader.TotalTime.TotalSeconds;

        t["read"] = DynValue.NewCallback((_, a) =>
        {
            if (closed)
                return DynValue.NewTable(new Table(s));

            var n = a.Count > 0 && a[0].Type == DataType.Number
                ? (int)a[0].Number
                : 65536;
            if (n < 1)
                return DynValue.NewTable(new Table(s));

            // 单次读取钳制：脚本传超大 n 会直接按 n 分配 float[]（n=2e9 → 8GB）打爆内存（审查 P2-9）
            if (n > 1_000_000)
                n = 1_000_000;

            var buf = new float[n];
            var read = reader.ReadSamples(buf, 0, n);

            var arr = new Table(s);
            for (var i = 0; i < read; i++)
                arr[i + 1] = buf[i];

            return DynValue.NewTable(arr);
        });

        t["seek"] = DynValue.NewCallback((_, a) =>
        {
            if (closed)
                return DynValue.False;
            if (a.Count < 1 || a[0].Type != DataType.Number)
                throw new ScriptRuntimeException("stream:seek(sampleIndex)");

            var pos = (long)a[0].Number;
            if (pos < 0 || pos > reader.TotalSamples)
                return DynValue.False;

            reader.SamplePosition = pos;
            return DynValue.True;
        });

        t["pos"] = DynValue.NewCallback((_, _) =>
            DynValue.NewNumber(closed ? 0 : reader.SamplePosition));

        t["close"] = DynValue.NewCallback((_, _) =>
        {
            if (closed)
                return DynValue.Nil;
            closed = true;
            try
            { reader.Dispose(); }
            catch (Exception ex)
            {
                LuaDiagnostics.WarnOnce("nvorbis stream:close 释放读取器失败", ex);
            }
            return DynValue.Nil;
        });

        return t;
    }
    private static void CheckEnabled()
    {
        if (!MainPlugin.CacheConfig.EnableNvorbis)
            throw new ScriptRuntimeException(
                "nvorbis: 未启用（Config.EnableNvorbis = false）");
    }
    private static string Resolve(LuaModInstance mod, string input)
        => LuaPathGuard.Resolve(mod, input, "nvorbis");
}
