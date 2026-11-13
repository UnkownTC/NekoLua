using System.IO.Compression;
using System.Text;

using MoonSharp.Interpreter;

using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.IO;

/// <summary>
/// compression 表（输入 text → UTF-8 → 压缩 → base64；反向同理）：
///   compression.gzip / gunzip(text)
///   compression.deflate / inflate(text)
/// </summary>
public class LuaCompressionProxy : IRegisterLua
{
    public string Name { get; } = "compression";

    // 解压输出配额：几 KB 密文可解出数 GB（解压炸弹），无上限会冻结主线程并 OOM（审查 P2-8）
    private const long MaxOutputBytes = 64 * 1024 * 1024;
    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);

        t["gzip"] = CompressCallback(Zip, "compression.gzip(text)");
        t["gunzip"] = DecompressCallback(Unzip, "compression.gunzip(base64)");
        t["deflate"] = CompressCallback(Deflate, "compression.deflate(text)");
        t["inflate"] = DecompressCallback(Inflate, "compression.inflate(base64)");

        return t;
    }
    static DynValue CompressCallback(Func<byte[], byte[]> f, string sig)
    {
        return DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException(sig);

            try
            {
                var bytes = Encoding.UTF8.GetBytes(args[0].String);
                var compressed = f(bytes);
                return DynValue.NewString(Convert.ToBase64String(compressed));
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"{sig}: {ex.Message}");
            }
        });
    }
    static DynValue DecompressCallback(Func<byte[], byte[]> f, string sig)
    {
        return DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException(sig);

            try
            {
                var compressed = Convert.FromBase64String(args[0].String);
                var bytes = f(compressed);
                return DynValue.NewString(Encoding.UTF8.GetString(bytes));
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"{sig}: {ex.Message}");
            }
        });
    }
    static byte[] Zip(byte[] input)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(input, 0, input.Length);
        return output.ToArray();
    }
    static byte[] Unzip(byte[] input)
    {
        using var ms = new MemoryStream(input);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        return CopyBounded(gz, "gunzip");
    }
    static byte[] Deflate(byte[] input)
    {
        using var output = new MemoryStream();
        using (var df = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            df.Write(input, 0, input.Length);
        return output.ToArray();
    }
    static byte[] Inflate(byte[] input)
    {
        using var ms = new MemoryStream(input);
        using var df = new DeflateStream(ms, CompressionMode.Decompress);
        return CopyBounded(df, "inflate");
    }

    private static byte[] CopyBounded(Stream source, string op)
    {
        using var output = new MemoryStream();
        var buf = new byte[81920];
        long total = 0;
        int n;

        while ((n = source.Read(buf, 0, buf.Length)) > 0)
        {
            total += n;
            if (total > MaxOutputBytes)
                throw new IOException($"{op}: 解压输出超过 {MaxOutputBytes / 1024 / 1024}MB 上限（疑似解压炸弹）");

            output.Write(buf, 0, n);
        }

        return output.ToArray();
    }
}
