using MoonSharp.Interpreter;
using NekoLua.Loader;
using NekoLua.Registers;
using System.Security.Cryptography;
using System.Text;

namespace NekoLua.Proxies.Security.Cryptography;

/// <summary>
/// crypto 表：
///   crypto.md5 / sha1 / sha256 / sha512(str)     -> hex
///   crypto.hmacSha256(key, data)                 -> hex
///   crypto.uuid()                                -- 标准 UUID v4
///   crypto.randomBytes(n)                        -> hex（n: 1-1024）
///
///   crypto.rsa.generate(bits)                    -> {public, private}（XML 字符串）
///   crypto.rsa.encrypt(publicKey, text)          -> base64
///   crypto.rsa.decrypt(privateKey, base64)       -> text
///   crypto.rsa.sign(privateKey, text)            -> base64（SHA-256 签名）
///   crypto.rsa.verify(publicKey, text, sig)      -> bool
///
///   crypto.aes.generateKey(bits)                 -- bits: 128/192/256
///   crypto.aes.generateIV()
///   crypto.aes.encrypt(key, text)                -- IV 自动前置
///   crypto.aes.decrypt(key, base64)
///   crypto.aes.encryptWithIV(key, ivBase64, text)
///   crypto.aes.decryptWithIV(key, ivBase64, base64)
/// </summary>
internal class LuaCryptoProxy : IRegisterLua
{
    public string Name { get; } = "crypto";
    public Table Create(LuaModInstance mod)
    {
        var t = new Table(mod.Script);

        t["md5"] = HashCallback(MD5.Create, "crypto.md5(str)");
        t["sha1"] = HashCallback(SHA1.Create, "crypto.sha1(str)");
        t["sha256"] = HashCallback(SHA256.Create, "crypto.sha256(str)");
        t["sha512"] = HashCallback(SHA512.Create, "crypto.sha512(str)");

        t["hmacSha256"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 ||
                args[0].Type != DataType.String ||
                args[1].Type != DataType.String)
                throw new ScriptRuntimeException("crypto.hmacSha256(key, data)");

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(args[0].String));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(args[1].String));
            return DynValue.NewString(ToHex(hash));
        });

        t["uuid"] = DynValue.NewCallback((_, _) =>
            DynValue.NewString(Guid.NewGuid().ToString("D")));

        t["randomBytes"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.Number)
                throw new ScriptRuntimeException("crypto.randomBytes(n)");

            var n = (int)args[0].Number;
            if (n is < 1 or > 1024)
                throw new ScriptRuntimeException("crypto.randomBytes(n): n 必须在 1-1024 之间");

            var buf = new byte[n];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(buf);
            return DynValue.NewString(ToHex(buf));
        });
        t["rsa"] = BuildRsa(mod.Script);
        t["aes"] = BuildAes(mod.Script);
        return t;
    }
    static DynValue HashCallback(Func<HashAlgorithm> factory, string sig)
    {
        return DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 1 || args[0].Type != DataType.String)
                throw new ScriptRuntimeException(sig);

            using var alg = factory();
            var hash = alg.ComputeHash(Encoding.UTF8.GetBytes(args[0].String));
            return DynValue.NewString(ToHex(hash));
        });
    }
    private static readonly string[] HexTable = BuildHexTable();
    private static string[] BuildHexTable()
    {
        var table = new string[256];
        for (var i = 0; i < 256; i++)
            table[i] = i.ToString("x2");
        return table;
    }
    static string ToHex(byte[] bytes)
    {
        var chars = new char[bytes.Length * 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            var h = HexTable[bytes[i]];
            chars[i * 2] = h[0];
            chars[(i * 2) + 1] = h[1];
        }
        return new string(chars);
    }
    static Table BuildRsa(Script script)
    {
        var t = new Table(script);

        t["generate"] = DynValue.NewCallback((_, args) =>
        {
            var bits = args.Count > 0 && args[0].Type == DataType.Number
                ? (int)args[0].Number : 2048;

            if (bits is < 512 or > 8192)
                throw new ScriptRuntimeException("crypto.rsa.generate: bits 应在 512~8192");

            try
            {
                using var rsa = RSA.Create(bits);
                return DynValue.NewTable(new Table(script)
                {
                    ["public"] = rsa.ToXmlString(false),
                    ["private"] = rsa.ToXmlString(true),
                });
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"crypto.rsa.generate: {ex.Message}");
            }
        });

        t["encrypt"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.String)
                throw new ScriptRuntimeException("crypto.rsa.encrypt(publicKey, text)");

            try
            {
                using var rsa = RSA.Create();
                rsa.FromXmlString(args[0].String);

                var data = Encoding.UTF8.GetBytes(args[1].String);
                var enc = rsa.Encrypt(data, RSAEncryptionPadding.Pkcs1);
                return DynValue.NewString(Convert.ToBase64String(enc));
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"crypto.rsa.encrypt: {ex.Message}");
            }
        });

        t["decrypt"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.String)
                throw new ScriptRuntimeException("crypto.rsa.decrypt(privateKey, base64)");

            try
            {
                using var rsa = RSA.Create();
                rsa.FromXmlString(args[0].String);

                var enc = Convert.FromBase64String(args[1].String);
                var dec = rsa.Decrypt(enc, RSAEncryptionPadding.Pkcs1);
                return DynValue.NewString(Encoding.UTF8.GetString(dec));
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"crypto.rsa.decrypt: {ex.Message}");
            }
        });

        t["sign"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.String)
                throw new ScriptRuntimeException("crypto.rsa.sign(privateKey, text)");

            try
            {
                using var rsa = RSA.Create();
                rsa.FromXmlString(args[0].String);

                var data = Encoding.UTF8.GetBytes(args[1].String);
                var sig = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                return DynValue.NewString(Convert.ToBase64String(sig));
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"crypto.rsa.sign: {ex.Message}");
            }
        });

        t["verify"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 3 ||
                args[0].Type != DataType.String ||
                args[1].Type != DataType.String ||
                args[2].Type != DataType.String)
                throw new ScriptRuntimeException("crypto.rsa.verify(publicKey, text, signature)");

            try
            {
                using var rsa = RSA.Create();
                rsa.FromXmlString(args[0].String);

                var data = Encoding.UTF8.GetBytes(args[1].String);
                var sig = Convert.FromBase64String(args[2].String);
                return DynValue.NewBoolean(
                    rsa.VerifyData(data, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            }
            catch
            {
                return DynValue.False;
            }
        });

        return t;
    }
    static Table BuildAes(Script script)
    {
        var t = new Table(script);

        t["generateKey"] = DynValue.NewCallback((_, args) =>
        {
            var bits = args.Count > 0 && args[0].Type == DataType.Number
                ? (int)args[0].Number : 256;

            if (bits is not 128 and not 192 and not 256)
                throw new ScriptRuntimeException("crypto.aes.generateKey: bits 只能是 128 / 192 / 256");

            using var aes = Aes.Create();
            aes.KeySize = bits;
            aes.GenerateKey();
            return DynValue.NewString(Convert.ToBase64String(aes.Key));
        });

        t["generateIV"] = DynValue.NewCallback((_, _) =>
        {
            using var aes = Aes.Create();
            aes.GenerateIV();
            return DynValue.NewString(Convert.ToBase64String(aes.IV));
        });

        t["encrypt"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.String)
                throw new ScriptRuntimeException("crypto.aes.encrypt(key, text)");

            try
            {
                var key = Convert.FromBase64String(args[0].String);

                using var aes = Aes.Create();
                aes.Key = key;
                aes.GenerateIV();

                using var enc = aes.CreateEncryptor();
                var plain = Encoding.UTF8.GetBytes(args[1].String);
                var cipher = enc.TransformFinalBlock(plain, 0, plain.Length);

                var result = new byte[aes.IV.Length + cipher.Length];
                Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
                Buffer.BlockCopy(cipher, 0, result, aes.IV.Length, cipher.Length);

                return DynValue.NewString(Convert.ToBase64String(result));
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"crypto.aes.encrypt: {ex.Message}");
            }
        });

        t["decrypt"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 2 || args[0].Type != DataType.String || args[1].Type != DataType.String)
                throw new ScriptRuntimeException("crypto.aes.decrypt(key, base64)");

            try
            {
                var key = Convert.FromBase64String(args[0].String);
                var raw = Convert.FromBase64String(args[1].String);

                if (raw.Length < 16)
                    throw new Exception("密文长度不足");

                var iv = new byte[16];
                var cipher = new byte[raw.Length - 16];
                Buffer.BlockCopy(raw, 0, iv, 0, 16);
                Buffer.BlockCopy(raw, 16, cipher, 0, cipher.Length);

                using var aes = Aes.Create();
                aes.Key = key;
                aes.IV = iv;

                using var dec = aes.CreateDecryptor();
                var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                return DynValue.NewString(Encoding.UTF8.GetString(plain));
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"crypto.aes.decrypt: {ex.Message}");
            }
        });

        t["encryptWithIV"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 3 ||
                args[0].Type != DataType.String ||
                args[1].Type != DataType.String ||
                args[2].Type != DataType.String)
                throw new ScriptRuntimeException("crypto.aes.encryptWithIV(key, ivBase64, text)");

            try
            {
                using var aes = Aes.Create();
                aes.Key = Convert.FromBase64String(args[0].String);
                aes.IV = Convert.FromBase64String(args[1].String);

                using var enc = aes.CreateEncryptor();
                var plain = Encoding.UTF8.GetBytes(args[2].String);
                var cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
                return DynValue.NewString(Convert.ToBase64String(cipher));
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"crypto.aes.encryptWithIV: {ex.Message}");
            }
        });

        t["decryptWithIV"] = DynValue.NewCallback((_, args) =>
        {
            if (args.Count < 3 ||
                args[0].Type != DataType.String ||
                args[1].Type != DataType.String ||
                args[2].Type != DataType.String)
                throw new ScriptRuntimeException("crypto.aes.decryptWithIV(key, ivBase64, base64)");

            try
            {
                using var aes = Aes.Create();
                aes.Key = Convert.FromBase64String(args[0].String);
                aes.IV = Convert.FromBase64String(args[1].String);

                using var dec = aes.CreateDecryptor();
                var cipher = Convert.FromBase64String(args[2].String);
                var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
                return DynValue.NewString(Encoding.UTF8.GetString(plain));
            }
            catch (Exception ex)
            {
                throw new ScriptRuntimeException($"crypto.aes.decryptWithIV: {ex.Message}");
            }
        });

        return t;
    }
}
