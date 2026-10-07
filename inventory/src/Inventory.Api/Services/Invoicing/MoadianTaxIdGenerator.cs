using System.Text;

namespace Inventory.Api.Services.Invoicing;

/// <summary>
/// سازندهٔ محلی «شماره منحصر به فرد مالیاتی» ۲۲ نویسه‌ای (taxid) مطابق قالب رسمی
/// RC_DCPS.SN و پیاده‌سازی SDK (TaxCollectData.Library.Business.TaxIdGenerator):
///
///   memoryId(6) + hex(روزهای گذشته از اپک، ۵ رقم) + hex(سریال، ۱۰ رقم) + رقم کنترل Verhoeff
///
/// رقم کنترل روی رشتهٔ دهدهی
///   toDecimal(memoryId) + روزها(۶ رقم) + سریال(۱۲ رقم)
/// حساب می‌شود که در toDecimal هر رقم خودش و هر حرف کد ASCII آن است.
/// جداول Verhoeff دقیقاً همان جداول SDK هستند و الگوریتم روی سه شمارهٔ مالیاتی
/// واقعی سنجیده شده است (3 از 3 صحیح).
///
/// این کلاس فقط «پشتیبان» است: سرویس ارسال، ابتدا تولیدکنندهٔ رسمی SDK را از
/// DI می‌گیرد و تنها در صورتی که در دسترس نباشد، به این پیاده‌سازی روی می‌آورد.
/// </summary>
internal static class MoadianTaxIdGenerator
{
    // جداول Verhoeff — همان TaxCollectData.Library.Business.VerhoffProvider
    private static readonly int[,] Multiplication =
    {
        {0, 1, 2, 3, 4, 5, 6, 7, 8, 9},
        {1, 2, 3, 4, 0, 6, 7, 8, 9, 5},
        {2, 3, 4, 0, 1, 7, 8, 9, 5, 6},
        {3, 4, 0, 1, 2, 8, 9, 5, 6, 7},
        {4, 0, 1, 2, 3, 9, 5, 6, 7, 8},
        {5, 9, 8, 7, 6, 0, 4, 3, 2, 1},
        {6, 5, 9, 8, 7, 1, 0, 4, 3, 2},
        {7, 6, 5, 9, 8, 2, 1, 0, 4, 3},
        {8, 7, 6, 5, 9, 3, 2, 1, 0, 4},
        {9, 8, 7, 6, 5, 4, 3, 2, 1, 0},
    };

    private static readonly int[,] Permutation =
    {
        {0, 1, 2, 3, 4, 5, 6, 7, 8, 9},
        {1, 5, 7, 6, 2, 8, 3, 0, 9, 4},
        {5, 8, 0, 3, 7, 9, 6, 1, 4, 2},
        {8, 9, 1, 6, 0, 4, 3, 5, 2, 7},
        {9, 4, 5, 3, 1, 2, 6, 8, 7, 0},
        {4, 2, 8, 6, 5, 7, 3, 9, 0, 1},
        {2, 7, 9, 3, 8, 0, 6, 4, 1, 5},
        {7, 0, 4, 6, 9, 1, 3, 2, 5, 8},
    };

    private static readonly int[] Inverse = { 0, 4, 3, 2, 1, 5, 6, 7, 8, 9 };

    public static string GenerateTaxId(string memoryId, long serial, DateTime createDate)
    {
        if (string.IsNullOrWhiteSpace(memoryId) || memoryId.Length < 6)
            throw new ArgumentException("شناسهٔ حافظهٔ مالیاتی معتبر نیست.", nameof(memoryId));

        var memory = memoryId.Trim().ToUpperInvariant()[..6];
        var timeDayRange = (int)(new DateTimeOffset(createDate).ToUnixTimeSeconds() / (3600 * 24));
        var hexTime = Convert.ToString(timeDayRange, 16);
        var hexSerial = Convert.ToString(Math.Max(0, serial), 16);
        var initial = $"{memory}{hexTime.PadLeft(5, '0')}{hexSerial.PadLeft(10, '0')}";
        var controlText = $"{ToDecimal(memory)}{timeDayRange.ToString().PadLeft(6, '0')}{Math.Max(0, serial).ToString().PadLeft(12, '0')}";
        return $"{initial}{GenerateVerhoeff(controlText)}".ToUpperInvariant();
    }

    /// <summary>سریال صورتحساب به قالب رسمی inno: هگزادسیمال، ۱۰ رقم، با صفرِ سمت چپ.</summary>
    public static string ToInno(long serial) => Convert.ToString(Math.Max(0, serial), 16).PadLeft(10, '0').ToUpperInvariant();

    private static string ToDecimal(string memoryId)
    {
        var sb = new StringBuilder();
        foreach (var ch in memoryId)
            sb.Append(char.IsDigit(ch) ? ch : (int)ch);
        return sb.ToString();
    }

    private static string GenerateVerhoeff(string num)
    {
        var c = 0;
        for (var i = 0; i < num.Length; i++)
        {
            var digit = num[num.Length - 1 - i] - '0';
            c = Multiplication[c, Permutation[(i + 1) % 8, digit]];
        }
        return Inverse[c].ToString();
    }
}
