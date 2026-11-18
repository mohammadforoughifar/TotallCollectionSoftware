using Inventory.Shared.Dtos;

namespace Inventory.Shared;

/// <summary>Optional Iranian shop identifiers: empty is allowed; supplied values must be complete.</summary>
/// <remarks>
/// قواعد یکسان برای «همهٔ فرمها»: کد پستی دقیقاً ۱۰ رقم و شمارهٔ تلفن/موبایل
/// دقیقاً ۱۱ رقم (بدون جداکننده، با پشتیبانی از ارقام فارسی و کد کشور ‎+۹۸‎).
/// این کلاس در لایهٔ Shared است تا هم کلاینت (قبل از ذخیره) و هم سرور
/// (قبل از هرگونه نوشتن در دیتابیس) دقیقاً یک قانون را اعمال کنند.
/// </remarks>
public static class ShopIdentifierRules
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Fa.ToEn(value).Trim().Replace(" ", "").Replace("-", "");
    }

    public static string? NormalizeIban(string? value)
    {
        var normalized = Normalize(value)?.ToUpperInvariant();
        return normalized?.StartsWith("IR", StringComparison.Ordinal) == true ? normalized[2..] : normalized;
    }

    /// <summary>
    /// نرمالسازی شمارهٔ تلفن: ارقام فارسی/عربی → لاتین، حذف جداکنندهها و
    /// تبدیل کد کشور (‎+۹۸‎ / ‎۰۰۹۸‎ / ‎۹۸‎) به پیشوند داخلی ‎۰‎.
    /// خالی بودن یعنی «فیلد اختیاری» و <c>null</c> برمیگرداند.
    /// </summary>
    public static string? NormalizePhone(string? value)
    {
        var phone = Normalize(value);
        if (phone is null) return null;
        phone = phone.Replace("(", "").Replace(")", "").Replace("+", "").Replace(".", "");
        if (phone.StartsWith("0098", StringComparison.Ordinal) && phone.Length > 4)
            phone = "0" + phone[4..];
        else if (phone.StartsWith("98", StringComparison.Ordinal) && phone.Length == 12)
            phone = "0" + phone[2..];
        return phone.Length == 0 ? null : phone;
    }

    public static bool IsDigits(string value) => value.All(c => c is >= '0' and <= '9');

    /// <summary>کد پستی اختیاری است؛ در صورت ورود باید دقیقاً ۱۰ رقم باشد.</summary>
    public static string? ValidatePostalCode(string? value, string label = "کد پستی")
    {
        var postal = Normalize(value);
        if (postal is null) return null;
        return postal.Length == 10 && IsDigits(postal)
            ? null : $"{label} باید دقیقاً ۱۰ رقم باشد.";
    }

    /// <summary>تلفن/موبایل اختیاری است؛ در صورت ورود باید دقیقاً ۱۱ رقم باشد.</summary>
    public static string? ValidatePhone(string? value, string label = "شماره تلفن")
    {
        var phone = NormalizePhone(value);
        if (phone is null) return null;
        return phone.Length == 11 && IsDigits(phone)
            ? null : $"{label} باید دقیقاً ۱۱ رقم باشد.";
    }

    /// <summary>اعتبارسنجی یکجای شماره تماس و کد پستی فرمهایی که Party نیستند.</summary>
    public static string? ValidateContact(string? phone, string? postalCode = null,
        string phoneLabel = "شماره تلفن", string postalLabel = "کد پستی")
        => ValidatePhone(phone, phoneLabel) ?? ValidatePostalCode(postalCode, postalLabel);

    public static string? ValidateBank(TrsAccount account)
    {
        account.CardNumber = Normalize(account.CardNumber);
        account.Iban = NormalizeIban(account.Iban);
        if (account.CardNumber is not null && (account.CardNumber.Length != 16 || !IsDigits(account.CardNumber)))
            return "شماره کارت باید دقیقاً ۱۶ رقم باشد.";
        if (account.Iban is not null && (account.Iban.Length != 24 || !IsDigits(account.Iban)))
            return "شماره شبا باید دقیقاً ۲۴ رقم بدون IR (یا ۲۶ کاراکتر با IR) باشد.";
        return null;
    }

    public static string? ValidateCheque(TrsCheque cheque)
    {
        cheque.SayadId = Normalize(cheque.SayadId);
        return cheque.SayadId is not null && (cheque.SayadId.Length != 16 || !IsDigits(cheque.SayadId))
            ? "شناسه صیاد باید دقیقاً ۱۶ رقم باشد." : null;
    }

    /// <summary>
    /// اعتبارسنجی طرف حساب برای «هر دو» نوع مشتری و تأمینکننده.
    /// مقادیر معتبر نرمال (بدون جداکننده و با ارقام لاتین) روی همان شیء نوشته میشوند
    /// تا دقیقاً همان چیزی که اعتبارسنجی شده است ذخیره شود.
    /// </summary>
    public static string? ValidateParty(Party party)
    {
        party.PostalCode = Normalize(party.PostalCode);
        party.TaxId = Normalize(party.TaxId);
        party.Mobile = NormalizePhone(party.Mobile);
        party.Phone = NormalizePhone(party.Phone);
        if (party.PostalCode is not null && (party.PostalCode.Length != 10 || !IsDigits(party.PostalCode)))
            return "کد پستی باید دقیقاً ۱۰ رقم باشد.";
        var mobileError = ValidatePhone(party.Mobile, "شماره موبایل");
        if (mobileError is not null) return mobileError;
        var phoneError = ValidatePhone(party.Phone, "شماره تلفن");
        if (phoneError is not null) return phoneError;
        if (party.TaxId is not null && (party.TaxId.Length is not (10 or 11 or 12 or 14) || !IsDigits(party.TaxId)))
            return "شناسه ملی/مالیاتی باید ۱۰، ۱۱، ۱۲ یا ۱۴ رقم باشد.";
        return null;
    }
}
