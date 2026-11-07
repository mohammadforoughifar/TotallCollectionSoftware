using System.Security.Cryptography;
using System.Text;

namespace Inventory.Api.Helpers.Moadian;

/// <summary>
/// رمزنگاری/رمزگشایی نام فایل‌ها (کلید خصوصی مودیان) با AES-CBC و کلید ثابت اپلیکیشن.
/// خروجی hex است و برای نام فایل در دیسک امن است؛ معکوس‌پذیر تا نام اصلی قابل بازیابی باشد.
/// </summary>
public static class SecurityHelper
{
    // کلید 32 بایتی ثابت (فقط برای هش‌کردن نام فایل، نه داده حساس)
    private static readonly byte[] Key =
    {
        0x4D, 0x6F, 0x61, 0x64, 0x69, 0x61, 0x6E, 0x2E, 0x4B, 0x65, 0x79, 0x4E, 0x61, 0x6D, 0x65, 0x21,
        0x32, 0x30, 0x32, 0x36, 0x2E, 0x56, 0x32, 0x37, 0x2E, 0x4C, 0x4F, 0x43, 0x4B, 0x41, 0x4C, 0x31
    };
    private static readonly byte[] Ivec =
    {
        0x53, 0x41, 0x4C, 0x54, 0x31, 0x32, 0x33, 0x34, 0x56, 0x45, 0x43, 0x54, 0x31, 0x32, 0x33, 0x34
    };

    public static string EncryptString(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = Key;
        aes.IV = Ivec;
        using var enc = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(plainText ?? "");
        var cipher = enc.TransformFinalBlock(bytes, 0, bytes.Length);
        return Convert.ToHexString(cipher).ToLowerInvariant();
    }

    public static string DecryptString(string hexCipher)
    {
        if (string.IsNullOrEmpty(hexCipher)) return "";
        try
        {
            using var aes = Aes.Create();
            aes.Key = Key;
            aes.IV = Ivec;
            using var dec = aes.CreateDecryptor();
            var cipher = Convert.FromHexString(hexCipher);
            var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return hexCipher;
        }
    }
}
