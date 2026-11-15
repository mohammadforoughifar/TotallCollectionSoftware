namespace Inventory.Client.Pages.Moadian;

/// <summary>شرکت انتخاب‌شدهٔ بخش مودیان (در طول نشست روی همهٔ صفحات مودیان مشترک است).</summary>
public class MoadianCompanyContext
{
    public int CpInfoId { get; private set; }
    public event Action? Changed;

    public void SetCpInfoId(int id)
    {
        CpInfoId = id;
        Changed?.Invoke();
    }
}
