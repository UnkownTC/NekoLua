using System.Text;
using MoonSharp.Interpreter;
using NekoLua.Api;
using NekoLua.Loader;
using NekoLua.Registers;

namespace NekoLua.Proxies.IO;

/// <summary>
/// file 表 —— 文件读写 + 二进制缓冲区：
///
///   文本读写：
///     file.read(path)                     -> string | nil
///     file.write(path, text)              -> bool
///     file.append(path, text)             -> bool
///
///   二进制读写（buffer）：
///     file.read(path, "buffer")           -> buffer | nil
///     file.write(path, buffer)            -> bool
///     file.append(path, buffer)           -> bool
///
///   buffer 构造：
///     file.buffer([bytesTable])           -> buffer
///     file.bufferFromHex(hex)             -> buffer
///     file.bufferFromBase64(b64)          -> buffer
///
///   buffer 实例（b 是上面构造的 buffer）：
///     b:writeByte / writeBytes / writeInt16/32/64
///     b:writeFloat / writeDouble / writeString
///     b:readByte / readBytes(n) / readInt16/32/64
///     b:readFloat / readDouble / readString(n)
///     b:length() / hex() / base64() / bytes()
///     b:clear() / reset()
///     b.position（读写）/ b.size
///
///   文件系统：
///     file.exists / delete / size / name / parent / join
/// </summary>
internal class LuaFileProxy : IRegisterLua
{
    public string Name { get; } = "file";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        // ============================================================
        // 文件系统
        // ============================================================

        t["read"] = DynValue.NewCallback((_, args) =>
        {
            var path = Resolve(mod, args, 0);

            // 模式参数：默认 "text"，可指定 "buffer"
            var mode = args.Count > 1 && args[1].Type == DataType.String
                ? args[1].String.ToLowerInvariant()
                : "text";

            if (!File.Exists(path)) return DynValue.Nil;

            return mode switch
            {
                "buffer" or "bin" or "binary" => DynValue.NewTable(BuildBuffer(s, [.. File.ReadAllBytes(path)])),
                _ => DynValue.NewString(File.ReadAllText(path)),
            };
        });

        t["write"] = DynValue.NewCallback((_, args) =>
        {
            var path = Resolve(mod, args, 0);
            EnsureDir(path);

            if (args.Count < 2) throw new ScriptRuntimeException("file.write(path, content)");

            if (args[1].Type == DataType.String)
                File.WriteAllText(path, args[1].String);
            else if (args[1].Type == DataType.Table)
                File.WriteAllBytes(path, ExtractBytes(args[1].Table));
            else
                File.WriteAllText(path, args[1].ToPrintString());

            return DynValue.True;
        });

        t["append"] = DynValue.NewCallback((_, args) =>
        {
            var path = Resolve(mod, args, 0);
            EnsureDir(path);

            if (args.Count < 2) throw new ScriptRuntimeException("file.append(path, content)");

            if (args[1].Type == DataType.String)
                File.AppendAllText(path, args[1].String);
            else if (args[1].Type == DataType.Table)
            {
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write);
                var bytes = ExtractBytes(args[1].Table);
                fs.Write(bytes, 0, bytes.Length);
            }
            else
                File.AppendAllText(path, args[1].ToPrintString());

            return DynValue.True;
        });

        t["exists"] = DynValue.NewCallback((_, args) =>
        {
            var path = Resolve(mod, args, 0);
            return DynValue.NewBoolean(File.Exists(path));
        });

        t["delete"] = DynValue.NewCallback((_, args) =>
        {
            var path = Resolve(mod, args, 0);
            if (!File.Exists(path)) return DynValue.False;

            File.Delete(path);
            return DynValue.True;
        });

        t["size"] = DynValue.NewCallback((_, args) =>
        {
            var path = Resolve(mod, args, 0);
            return File.Exists(path)
                ? DynValue.NewNumber(new FileInfo(path).Length)
                : DynValue.Nil;
        });

        t["name"] = DynValue.NewCallback((_, args) =>
        {
            var path = Resolve(mod, args, 0);
            return DynValue.NewString(Path.GetFileName(path));
        });

        t["parent"] = DynValue.NewCallback((_, args) =>
        {
            var path = Resolve(mod, args, 0);
            var parent = Path.GetDirectoryName(path);
            return DynValue.NewString(parent ?? "");
        });

        t["join"] = DynValue.NewCallback((_, args) =>
        {
            var parts = new List<string>(args.Count);
            for (var i = 0; i < args.Count; i++)
            {
                if (args[i].Type != DataType.String)
                    throw new ScriptRuntimeException("file.join(...): 所有参数需要字符串");
                parts.Add(args[i].String);
            }
            return DynValue.NewString(Path.Combine([.. parts]));
        });

        t["buffer"] = DynValue.NewCallback((_, args) =>
        {
            var bytes = new List<byte>();

            if (args.Count > 0 && args[0].Type == DataType.Table)
            {
                var arr = args[0].Table;
                for (var i = 1; i <= arr.Length; i++)
                {
                    var v = arr.Get(i);
                    if (v.Type == DataType.Number) bytes.Add((byte)(int)v.Number);
                }
            }

            return DynValue.NewTable(BuildBuffer(s, bytes));
        });

        t["bufferFromHex"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("file.bufferFromHex(hex)");

            var hex = args[0].String;
            if (hex.Length % 2 != 0)
                throw new ScriptRuntimeException("file.bufferFromHex: 长度必须是偶数");

            var bytes = new List<byte>(hex.Length / 2);
            for (var i = 0; i < hex.Length; i += 2)
                bytes.Add(Convert.ToByte(hex.Substring(i, 2), 16));

            return DynValue.NewTable(BuildBuffer(s, bytes));
        });

        t["bufferFromBase64"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("file.bufferFromBase64(str)");

            try
            {
                var bytes = Convert.FromBase64String(args[0].String);
                return DynValue.NewTable(BuildBuffer(s, [.. bytes]));
            }
            catch
            {
                throw new ScriptRuntimeException("file.bufferFromBase64: 非法 base64");
            }
        });

        return t;
    }
    private static Table BuildBuffer(Script script, List<byte> data)
    {
        var t = new Table(script);
        var meta = new Table(script);

        var readPos = 0;

        t["writeByte"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("buffer:writeByte(n)");
            data.Add((byte)(int)args[0].Number);
            return DynValue.Nil;
        });

        t["writeBytes"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Table)
                throw new ScriptRuntimeException("buffer:writeBytes(table)");

            var arr = args[0].Table;
            for (var i = 1; i <= arr.Length; i++)
            {
                var v = arr.Get(i);
                if (v.Type == DataType.Number) data.Add((byte)(int)v.Number);
            }
            return DynValue.Nil;
        });

        t["writeInt16"] = WriteNum(2, n =>
        {
            var v = (short)n;
            data.Add((byte)(v >> 8));
            data.Add((byte)v);
        });

        t["writeInt32"] = WriteNum(4, n =>
        {
            var v = (int)n;
            data.Add((byte)(v >> 24));
            data.Add((byte)(v >> 16));
            data.Add((byte)(v >> 8));
            data.Add((byte)v);
        });

        t["writeInt64"] = WriteNum(8, n =>
        {
            var v = BitConverter.GetBytes((long)n);
            if (BitConverter.IsLittleEndian) Array.Reverse(v);
            data.AddRange(v);
        });

        t["writeFloat"] = WriteNum(4, n =>
        {
            var v = BitConverter.GetBytes((float)n);
            if (BitConverter.IsLittleEndian) Array.Reverse(v);
            data.AddRange(v);
        });

        t["writeDouble"] = WriteNum(8, n =>
        {
            var v = BitConverter.GetBytes(n);
            if (BitConverter.IsLittleEndian) Array.Reverse(v);
            data.AddRange(v);
        });

        t["writeString"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("buffer:writeString(s)");
            data.AddRange(Encoding.UTF8.GetBytes(args[0].String));
            return DynValue.Nil;
        });

        t["readByte"] = DynValue.NewCallback((_, _) =>
            readPos >= data.Count
                ? DynValue.Nil
                : DynValue.NewNumber(data[readPos++]));

        t["readBytes"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("buffer:readBytes(n)");

            var n = (int)args[0].Number;
            if (readPos + n > data.Count)
                throw new ScriptRuntimeException("buffer:readBytes: 超出范围");

            var arr = new Table(script);
            for (var i = 0; i < n; i++)
                arr[i + 1] = data[readPos++];

            return DynValue.NewTable(arr);
        });

        t["readInt16"] = ReadNum(2, b => (short)((b[0] << 8) | b[1]));
        t["readInt32"] = ReadNum(4, b =>
            (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
        t["readInt64"] = ReadNum(8, b =>
        {
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToInt64(b, 0);
        });
        t["readFloat"] = ReadNum(4, b =>
        {
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToSingle(b, 0);
        });
        t["readDouble"] = ReadNum(8, b =>
        {
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToDouble(b, 0);
        });

        t["readString"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("buffer:readString(n)");

            var n = (int)args[0].Number;
            if (readPos + n > data.Count)
                throw new ScriptRuntimeException("buffer:readString: 超出范围");

            var s = Encoding.UTF8.GetString([.. data], readPos, n);
            readPos += n;
            return DynValue.NewString(s);
        });

        t["length"] = DynValue.NewCallback((_, _) => DynValue.NewNumber(data.Count));

        t["hex"] = DynValue.NewCallback((_, _) =>
        {
            var sb = new StringBuilder(data.Count * 2);
            foreach (var b in data) sb.Append(b.ToString("x2"));
            return DynValue.NewString(sb.ToString());
        });

        t["base64"] = DynValue.NewCallback((_, _) =>
            DynValue.NewString(Convert.ToBase64String([.. data])));

        t["bytes"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(script);
            for (var i = 0; i < data.Count; i++)
                arr[i + 1] = data[i];
            return DynValue.NewTable(arr);
        });

        t["clear"] = DynValue.NewCallback((_, _) =>
        {
            data.Clear();
            readPos = 0;
            return DynValue.Nil;
        });

        t["reset"] = DynValue.NewCallback((_, _) =>
        {
            readPos = 0;
            return DynValue.Nil;
        });

        // ---- 元表 ----

        meta["__index"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[1].Type != DataType.String)
                return DynValue.Nil;

            return args[1].String switch
            {
                "position" => DynValue.NewNumber(readPos),
                "size" => DynValue.NewNumber(data.Count),
                _ => t.Get(args[1].String),
            };
        });

        meta["__newindex"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 3 || args[1].Type != DataType.String)
                return DynValue.Nil;

            if (args[1].String == "position" && args[2].Type == DataType.Number)
                readPos = (int)args[2].Number;

            return DynValue.Nil;
        });

        t.MetaTable = meta;
        return t;

        // ---- 局部辅助 ----

        DynValue WriteNum(int _unused, Action<double> writer)
        {
            return DynValue.NewCallback((_, args) =>
            {
                if (args.Count < 1 || args[0].Type != DataType.Number)
                    throw new ScriptRuntimeException("buffer:write: 需要数字");

                writer(args[0].Number);
                return DynValue.Nil;
            });
        }

        DynValue ReadNum(int size, Func<byte[], object> reader)
        {
            return DynValue.NewCallback((_, _) =>
            {
                if (readPos + size > data.Count)
                    throw new ScriptRuntimeException("buffer:read: 超出范围");

                var buf = new byte[size];
                data.CopyTo(readPos, buf, 0, size);
                readPos += size;

                var v = reader(buf);
                return v switch
                {
                    short s => DynValue.NewNumber(s),
                    int i => DynValue.NewNumber(i),
                    long l => DynValue.NewNumber(l),
                    float f => DynValue.NewNumber(f),
                    double d => DynValue.NewNumber(d),
                    _ => DynValue.Nil,
                };
            });
        }
    }

    private static byte[] ExtractBytes(Table bufTable)
    {
        var hexFn = bufTable.MetaTable?.Get("__index");
        if (hexFn?.Type != DataType.Function)
            throw new ScriptRuntimeException("file.write: 参数 table 不是 buffer");

        var hexResult = hexFn.Function.Call(
            DynValue.NewTable(bufTable),
            DynValue.NewString("hex"));

        if (hexResult.Type != DataType.Function)
            throw new ScriptRuntimeException("file.write: 参数 table 不是 buffer");

        var hex = hexResult.Function.Call().String;
        if (string.IsNullOrEmpty(hex)) return [];

        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

        return bytes;
    }
    private static string Resolve(LuaModInstance mod, CallbackArguments args, int index)
    {
        return args.Count <= index || args[index].Type != DataType.String
            ? throw new ScriptRuntimeException("file: 路径参数需要字符串")
            : LuaPathGuard.Resolve(mod, args[index].String, "file");
    }

    private static void EnsureDir(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }
}