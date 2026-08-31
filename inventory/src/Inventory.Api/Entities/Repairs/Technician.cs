using System.ComponentModel.DataAnnotations;
using Inventory.Shared;
namespace Inventory.Api.Data;

/// <summary>تعمیرکار مجموعه</summary>
public class Technician
{
    public int Id { get; set; }

    [MaxLength(150)]
    public string Name { get; set; } = "";

    [MaxLength(50)]
    public string? Phone { get; set; }

    /// <summary>تخصص (لپ‌تاپ، موبایل، دوربین، ...)</summary>
    [MaxLength(150)]
    public string? Specialty { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}