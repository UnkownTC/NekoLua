using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;
using System.Text;

namespace NekoLua.Proxies;

/// <summary>
/// buffer 表（大端序读写）：
///   buffer.new() / fromHex(str) / fromBase64(str)
///
///   b:writeByte / writeBytes(table) / writeInt16 / writeInt32 / writeInt64
///      writeFloat / writeDouble / writeString
///   b:readByte / readBytes(n) / readInt16 / readInt32 / readInt64
///      readFloat / readDouble / readString(n)
///   b:length() / hex() / base64() / bytes()
///   b:clear() / reset()
///   b.position（读写）/ b.size
/// </summary>
internal class LuaBufferProxy : IRegisterLua
{
    public string Name { get; } = "buffer";

    public Table Create(LuaModInstance mod)
    {
        var s = mod.Script;
        var t = new Table(s);

        t["new"] = DynValue.NewCallback((_, _) =>
            DynValue.NewTable(BuildBuffer(s, [])));

        t["fromHex"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("buffer.fromHex(str)");

            var hex = args[0].String;
            if (hex.Length % 2 != 0)
                throw new ScriptRuntimeException("buffer.fromHex: 长度必须是偶数");

            var bytes = new List<byte>(hex.Length / 2);
            for (var i = 0; i < hex.Length; i += 2)
                bytes.Add(Convert.ToByte(hex.Substring(i, 2), 16));

            return DynValue.NewTable(BuildBuffer(s, bytes));
        });

        t["fromBase64"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("buffer.fromBase64(str)");

            try
            {
                var bytes = Convert.FromBase64String(args[0].String);
                return DynValue.NewTable(BuildBuffer(s, [.. bytes]));
            }
            catch
            {
                throw new ScriptRuntimeException("buffer.fromBase64: 非法 base64");
            }
        });

        return t;
    }
    private static Table BuildBuffer(Script script, List<byte> data)
    {
        var t = new Table(script);
        var meta = new Table(script);

        var writePos = data.Count;
        var readPos = 0;


        t["writeByte"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("buffer.writeByte(n)");
            data.Add((byte)(int)args[0].Number);
            writePos = data.Count;
            return DynValue.Nil;
        });

        t["writeBytes"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Table)
                throw new ScriptRuntimeException("buffer.writeBytes(table)");

            var arr = args[0].Table;
            for (var i = 1; i <= arr.Length; i++)
            {
                var v = arr.Get(i);
                if (v.Type == DataType.Number)
                    data.Add((byte)(int)v.Number);
            }
            writePos = data.Count;
            return DynValue.Nil;
        });

        t["writeInt16"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("buffer.writeInt16(n)");
            var b = BitConverter.GetBytes((short)args[0].Number);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(b);
            data.AddRange(b);
            writePos = data.Count;
            return DynValue.Nil;
        });

        t["writeInt32"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("buffer.writeInt32(n)");

            var v = (int)args[0].Number;
            data.Add((byte)(v >> 24));
            data.Add((byte)(v >> 16));
            data.Add((byte)(v >> 8));
            data.Add((byte)v);
            writePos = data.Count;
            return DynValue.Nil;
        });

        t["writeInt64"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("buffer.writeInt64(n)");
            var b = BitConverter.GetBytes((long)args[0].Number);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(b);
            data.AddRange(b);
            writePos = data.Count;
            return DynValue.Nil;
        });

        t["writeFloat"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("buffer.writeFloat(n)");
            var b = BitConverter.GetBytes((float)args[0].Number);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(b);
            data.AddRange(b);
            writePos = data.Count;
            return DynValue.Nil;
        });

        t["writeDouble"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("buffer.writeDouble(n)");
            var b = BitConverter.GetBytes(args[0].Number);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(b);
            data.AddRange(b);
            writePos = data.Count;
            return DynValue.Nil;
        });

        t["writeString"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException("buffer.writeString(s)");
            data.AddRange(Encoding.UTF8.GetBytes(args[0].String));
            writePos = data.Count;
            return DynValue.Nil;
        });

        t["readByte"] = DynValue.NewCallback((_, _) =>
        {
            return readPos >= data.Count ? DynValue.Nil : DynValue.NewNumber(data[readPos++]);
        });

        t["readBytes"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("buffer.readBytes(n)");
            var n = (int)args[0].Number;
            if (readPos + n > data.Count)
                throw new ScriptRuntimeException("buffer.readBytes: 超出范围");

            var arr = new Table(script);
            for (var i = 0; i < n; i++)
                arr[i + 1] = data[readPos++];
            return DynValue.NewTable(arr);
        });

        t["readInt16"] = ReadNum(2, b => BitConverter.ToInt16(Be(b), 0));
        t["readInt32"] = DynValue.NewCallback((_, _) =>
        {
            if (readPos + 4 > data.Count)
                throw new ScriptRuntimeException("buffer.readInt32: 超出范围");

            var v = (data[readPos] << 24)
                  | (data[readPos + 1] << 16)
                  | (data[readPos + 2] << 8)
                  | data[readPos + 3];
            readPos += 4;
            return DynValue.NewNumber(v);
        });
        t["readInt64"] = ReadNum(8, b => BitConverter.ToInt64(Be(b), 0));
        t["readFloat"] = ReadNum(4, b => BitConverter.ToSingle(Be(b), 0));
        t["readDouble"] = ReadNum(8, b => BitConverter.ToDouble(Be(b), 0));

        t["readString"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("buffer.readString(n)");
            var n = (int)args[0].Number;
            if (readPos + n > data.Count)
                throw new ScriptRuntimeException("buffer.readString: 超出范围");

            var s = Encoding.UTF8.GetString([.. data], readPos, n);
            readPos += n;
            return DynValue.NewString(s);
        });

        t["length"] = DynValue.NewCallback((_, _) => DynValue.NewNumber(data.Count));

        t["hex"] = DynValue.NewCallback((_, _) =>
        {
            var sb = new StringBuilder(data.Count * 2);
            foreach (var b in data)
                _ = sb.Append(b.ToString("x2"));
            return DynValue.NewString(sb.ToString());
        });

        t["base64"] = DynValue.NewCallback((_, _) =>
            DynValue.NewString(Convert.ToBase64String([.. data])));

        t["clear"] = DynValue.NewCallback((_, _) =>
        {
            data.Clear();
            writePos = 0;
            readPos = 0;
            return DynValue.Nil;
        });

        t["reset"] = DynValue.NewCallback((_, _) =>
        {
            readPos = 0;
            return DynValue.Nil;
        });

        t["bytes"] = DynValue.NewCallback((_, _) =>
        {
            var arr = new Table(script);
            for (var i = 0; i < data.Count; i++)
                arr[i + 1] = data[i];
            return DynValue.NewTable(arr);
        });

        // ---- position 属性 ----

        meta["__index"] = DynValue.NewCallback((_, args) =>
        {
            return args.Count < 2 || args[1].Type != DataType.String
                ? DynValue.Nil
                : args[1].String switch
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

        static byte[] Be(byte[] b)
        {
            if (BitConverter.IsLittleEndian)
                Array.Reverse(b);
            return b;
        }

        DynValue ReadNum(int size, Func<byte[], object> f)
        {
            return DynValue.NewCallback((_, _) =>
            {
                if (readPos + size > data.Count)
                    throw new ScriptRuntimeException("buffer.read: 超出范围");

                var buf = new byte[size];
                data.CopyTo(readPos, buf, 0, size);
                readPos += size;

                var v = f(buf);
                return v switch
                {
                    short s => DynValue.NewNumber(s),
                    int i => DynValue.NewNumber(i),
                    long l => DynValue.NewNumber(l),
                    float fl => DynValue.NewNumber(fl),
                    double d => DynValue.NewNumber(d),
                    _ => DynValue.Nil,
                };
            });
        }
    }
}
