namespace SupportPanel.Data;

/// <summary>مشتری — یک ردیف به‌ازای هر قرارداد.</summary>
public class Customer
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? NationalId { get; set; }
    public string? Address { get; set; }
    public int? MergedIntoId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public List<ContactPerson> Contacts { get; set; } = new();
}

public class ContactPerson
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Title { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SupportUser
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public SupportRole Role { get; set; }
    public string PasswordHash { get; set; } = "";
    public int PasswordIterations { get; set; }
    public bool MustChangePassword { get; set; }
    public int FailedCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

/// <summary>ممیزی فقط‌افزودی با زنجیرهٔ هش (تغییر/حذف ردیف میانی = زنجیرهٔ شکسته).</summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public int? UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string? EntityId { get; set; }
    public string? DataJson { get; set; }
    public string PrevHash { get; set; } = "";
    public string Hash { get; set; } = "";
}

public class MailTemplate
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
}

public class Setting
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}
