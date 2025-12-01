using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public static class CryptoUtil
{
    // 프로젝트마다 꼭 바꿔 (길고 랜덤하게)
    private const string AppPepper = "CHANGE_THIS_TO_A_LONG_RANDOM_SECRET_2025";

    private static byte[] DeriveKey(string uid, byte[] salt, int keyBytes)
    {
        using var kdf = new Rfc2898DeriveBytes(uid + "|" + AppPepper, salt, 100_000, HashAlgorithmName.SHA256);
        return kdf.GetBytes(keyBytes);
    }

    // Base64( salt16 | iv16 | cipher... | hmac32 )
    public static string EncryptToBase64(string plainText, string uid)
    {
        byte[] salt = new byte[16];
        byte[] iv = new byte[16];

        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
            rng.GetBytes(iv);
        }

        byte[] encKey = DeriveKey(uid, salt, 32);
        byte[] macKey = DeriveKey(uid, salt, 32);

        byte[] cipher;
        using (var aes = Aes.Create())
        {
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = encKey;
            aes.IV = iv;

            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                var plain = Encoding.UTF8.GetBytes(plainText ?? "");
                cs.Write(plain, 0, plain.Length);
            }
            cipher = ms.ToArray();
        }

        byte[] payload = Combine(salt, iv, cipher);

        byte[] tag;
        using (var hmac = new HMACSHA256(macKey))
            tag = hmac.ComputeHash(payload);

        return Convert.ToBase64String(Combine(payload, tag));
    }

    public static string DecryptFromBase64(string base64, string uid)
    {
        var all = Convert.FromBase64String(base64);
        if (all.Length < 16 + 16 + 32) throw new Exception("Cipher too short.");

        byte[] salt = Slice(all, 0, 16);
        byte[] iv = Slice(all, 16, 16);
        byte[] tag = Slice(all, all.Length - 32, 32);

        byte[] payload = Slice(all, 0, all.Length - 32);
        byte[] cipher = Slice(all, 32, payload.Length - 32);

        byte[] encKey = DeriveKey(uid, salt, 32);
        byte[] macKey = DeriveKey(uid, salt, 32);

        using (var hmac = new HMACSHA256(macKey))
        {
            var expect = hmac.ComputeHash(payload);
            if (!FixedTimeEquals(expect, tag))
                throw new Exception("HMAC failed (tampered or wrong key).");
        }

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = encKey;
        aes.IV = iv;

        using var ms = new MemoryStream(cipher);
        using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using var sr = new StreamReader(cs, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    private static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    private static byte[] Slice(byte[] src, int offset, int len)
    {
        var dst = new byte[len];
        Buffer.BlockCopy(src, offset, dst, 0, len);
        return dst;
    }

    private static byte[] Combine(params byte[][] arrays)
    {
        int total = 0;
        foreach (var a in arrays) total += a.Length;
        var res = new byte[total];
        int o = 0;
        foreach (var a in arrays)
        {
            Buffer.BlockCopy(a, 0, res, o, a.Length);
            o += a.Length;
        }
        return res;
    }
}
