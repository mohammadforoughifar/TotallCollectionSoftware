using System.ComponentModel.DataAnnotations;

namespace Inventory.Api.Data;

/// <summary>
/// SMS permission for one company and one integrated module. Missing rows are disabled.
/// Provider credentials remain in server configuration, never in this table.
/// </summary>
public sealed class CompanySmsSetting
{
    public int CompanyId { get; set; }
    [MaxLength(80)] public string ModuleKey { get; set; } = "";
    public bool Enabled { get; set; }
    // Changes whenever this module permission is toggled; queued events capture this value.
    public long Revision { get; set; } = 1;
    public DateTime UpdatedAtUtc { get; set; }
    [MaxLength(120)] public string? UpdatedBy { get; set; }
}
