using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;
public class AppSetting
{
    public int Id { get; set; }
    [MaxLength(20)] public string CostingMethod { get; set; } = "Average";
    public bool AllowNegativeStock { get; set; }

    /// <summary>آدرس سرور مرکزی درخواست‌های IT — خالی یعنی همین سرور، مرکزی است</summary>
    public string? ItServerUrl { get; set; }

    /// <summary>نام این شرکت (برای ارسال درخواست به سرور مرکزی)</summary>
    public string? ItCompanyName { get; set; }

    /// <summary>توکن ربات بله (tapi.bale.ai)</summary>
    public string? BaleBotToken { get; set; }

    /// <summary>توکن ایتایار (eitaayar.ir)</summary>
    public string? EitaaToken { get; set; }

    /// <summary>شماره معرف سامانه — در امضای پیام‌ها می‌آید</summary>
    [MaxLength(20)] public string? MessengerSenderNumber { get; set; } = "09111189771";

    /// <summary>مدت نمایش اعلان زندهٔ بالای صفحه (میلی‌ثانیه). ۰ = تا بستن دستی. پیش‌فرض ۲ دقیقه.</summary>
    public int NotifyBannerMs { get; set; } = 120_000;

    /// <summary>مدت نمایش پیام کوتاه/توست (میلی‌ثانیه). ۰ = تا بستن دستی. پیش‌فرض ۲۰ ثانیه.</summary>
    public int NotifyToastMs { get; set; } = 20_000;
}