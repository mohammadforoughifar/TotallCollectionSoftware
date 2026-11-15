namespace Inventory.Shared.Dtos;

/// <summary>Non-secret SMS permissions for the authenticated user's active company.</summary>
public sealed class CompanySmsSettingsDto
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = "";
    public List<CompanySmsModuleDto> Modules { get; set; } = new();
}

public sealed class CompanySmsModuleDto
{
    public string Key { get; set; } = "";
    public string Group { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Enabled { get; set; }
}

/// <summary>CompanyId checks the page context; it never selects another company.</summary>
public sealed class SaveCompanySmsSettingsDto
{
    public int CompanyId { get; set; }
    public List<SaveCompanySmsModuleDto> Modules { get; set; } = new();
}

public sealed class SaveCompanySmsModuleDto
{
    public string Key { get; set; } = "";
    public bool Enabled { get; set; }
}
