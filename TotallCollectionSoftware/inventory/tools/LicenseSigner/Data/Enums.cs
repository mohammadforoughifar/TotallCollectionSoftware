namespace LicenseSigner.Data;

/// <summary>وضعیت درخواست. «صادرشده» و بعدش فقط از مسیر امضا/تحویل پر می‌شوند.</summary>
public enum RequestStatus
{
    New = 0,
    Reviewing = 1,
    NeedsInfo = 2,
    Approved = 3,
    Issued = 4,
    Delivered = 5,
    Closed = 6,
    Rejected = 7,
    Cancelled = 8,
    Duplicate = 9
}

public enum RequestType
{
    NewActivation = 0,
    Renewal = 1,
    AddModule = 2,
    QuotaPack = 3,
    Rehost = 4,
    Trial = 5,
    Bridge = 6,
    Other = 7
}

public enum RequestChannel
{
    Email = 0,
    Paste = 1,
    Manual = 2,
    Phone = 3,
    InPerson = 4,
    Other = 5
}

public enum SupportRole
{
    Operator = 0,
    Signer = 1,
    Admin = 2
}

public enum IdentityMethod
{
    Phone = 0,
    Email = 1,
    Sms = 2,
    InPerson = 3,
    Other = 4
}

public enum DeliveryChannel
{
    EmailSmtp = 0,
    EmlDraft = 1,
    Download = 2,
    Manual = 3
}

public enum LicenseStatus
{
    Current = 0,
    Expired = 1,
    Superseded = 2,
    RevokedNote = 3
}
