using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// 문자열을 Base64로 암복호화하는 유틸.
/// 구성:
/// 1) salt(16) + iv(16) 를 랜덤 생성
/// 2) uid + AppPepper 기반으로 PBKDF2로 키 파생
/// 3) AES-CBC로 암호화
/// 4) (salt|iv|cipher)를 HMACSHA256으로 해시 → tag(32)
/// 5) 최종 저장 포맷: Base64( salt16 | iv16 | cipher... | hmac32 )
///
/// 특징:
/// - salt/iv가 매번 랜덤이라 같은 평문도 암호문이 매번 달라짐.
/// - HMAC 검증으로 "변조 방지" 가능.
/// </summary>
public static class CryptoUtil
{
    /// <summary>
    /// 앱 고정 비밀값(pepper).
    /// - 서버의 secret처럼 '코드 밖'에서 안전하게 숨기는 게 이상적이지만,
    ///   클라이언트(유니티)에서는 완벽한 은닉이 불가능함.
    /// - 최소한 프로젝트마다 반드시 변경하고, 충분히 길고 랜덤해야 함.
    /// </summary>
    private const string AppPepper = "CHANGE_THIS_TO_A_LONG_RANDOM_SECRET_2025";

    /// <summary>
    /// PBKDF2(Rfc2898DeriveBytes)로 키를 파생한다.
    ///
    /// 입력:
    /// - uid: 사용자마다 다른 값(예: Firebase UID). 사용자별로 다른 키가 나오게 함.
    /// - salt: 랜덤 16바이트. 같은 uid라도 매번 다른 키가 나오게 함.
    /// - keyBytes: 만들어낼 키 길이(바이트). AES-256이면 32바이트.
    ///
    /// PBKDF2 목적:
    /// - uid 작은 변화에도 키가 완전히 달라지게 함
    /// - 반복(iterations=100k)으로 brute force 비용을 올림
    /// </summary>
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

    /// <summary>
    /// Base64( salt|iv|cipher|tag )를 입력받아 복호화한다.
    /// 순서:
    /// 1) Base64 디코딩
    /// 2) salt/iv/tag/cipher 분해
    /// 3) uid+salt로 키 파생
    /// 4) HMAC 검증(위변조/키 불일치 검사)
    /// 5) 통과하면 AES 복호화
    /// </summary>
    public static string DecryptFromBase64(string base64, string uid)
    {
        // Base64 -> raw bytes
        var all = Convert.FromBase64String(base64);
        // 최소 길이 검증:
        // salt16 + iv16 + tag32 = 64바이트는 최소로 있어야 함
        if (all.Length < 16 + 16 + 32) throw new Exception("Cipher too short.");

        byte[] salt = Slice(all, 0, 16);
        byte[] iv = Slice(all, 16, 16);
        byte[] tag = Slice(all, all.Length - 32, 32);

        byte[] payload = Slice(all, 0, all.Length - 32);
        byte[] cipher = Slice(all, 32, payload.Length - 32);

        byte[] encKey = DeriveKey(uid, salt, 32);
        byte[] macKey = DeriveKey(uid, salt, 32);

        //  먼저 HMAC 검증: 복호화 전에 변조 여부부터 확인해야 안전
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

    /// <summary>
    /// 바이트 배열을 "시간 일정하게" 비교.
    /// 일반 비교는 앞에서부터 달라지면 빨리 종료되어 timing leak 가능성이 있어
    /// 가능한 한 모든 바이트를 끝까지 비교한다.
    /// </summary>
    private static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    /// <summary>
    /// src[offset..offset+len) 구간을 잘라 새 배열로 반환.
    /// </summary>
    private static byte[] Slice(byte[] src, int offset, int len)
    {
        var dst = new byte[len];
        Buffer.BlockCopy(src, offset, dst, 0, len);
        return dst;
    }

    /// <summary>
    /// 여러 바이트 배열을 한 배열로 이어붙이기.
    /// </summary>
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
