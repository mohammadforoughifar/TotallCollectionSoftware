namespace Inventory.Client.Services.RadisHr;

/// <summary>
/// نمای منابع انسانی از همان نشست Inventory؛ نه توکن، نه localStorage و نه ورود جداگانه.
/// RadisHr.Access مطابق نسخه قبلی مجوز کامل ماژول است.
/// </summary>
public sealed class RadisHrAuthState(IAuthState auth)
{
    public bool HasAccess => auth.IsLoggedIn && (auth.IsAdmin || auth.Has("RadisHr.Access"));
    public string UserKey => !HasAccess ? "" : auth.IsAdmin ? "ceo" : "hr";
    public string DisplayName => auth.DisplayName;
    public string RoleTitle => auth.IsAdmin ? "مدیر سامانه" : "منابع انسانی";
}
