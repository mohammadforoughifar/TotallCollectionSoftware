using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;
public class SystemCompany
{
    public int Id { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(100)] public string? Code { get; set; }
    [MaxLength(50)] public string? Phone { get; set; }
    [MaxLength(250)] public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// نام فایل PDF سربرگ شرکت — فایل باید در مسیر روت API قرار داشته باشد
    /// (مثال: «letterhead-forough.pdf» در کنار فایل اجرایی/روت پروژه API).
    /// هنگام چاپ نامه صادره، متن نامه روی همین سربرگ قرار می‌گیرد.
    /// </summary>
    [MaxLength(200)] public string? LetterheadFileName { get; set; }
}