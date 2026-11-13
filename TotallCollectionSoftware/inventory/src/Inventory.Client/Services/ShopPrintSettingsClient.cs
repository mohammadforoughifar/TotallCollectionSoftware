using Inventory.Shared.Dtos;
namespace Inventory.Client.Services;
public interface IShopPrintSettingsClient
{
    Task<ShopPrintSettingsDto> GetAsync();
    Task<ShopPrintSettingsDto> SaveAsync(ShopPrintSettingsDto settings);
}
public class ShopPrintSettingsClient : IShopPrintSettingsClient
{
    private readonly IApiClient _api;
    public ShopPrintSettingsClient(IApiClient api) => _api = api;
    public Task<ShopPrintSettingsDto> GetAsync() => _api.GetAsync<ShopPrintSettingsDto>("api/shop-print-settings");
    public Task<ShopPrintSettingsDto> SaveAsync(ShopPrintSettingsDto settings) => _api.PutAsync<ShopPrintSettingsDto>("api/shop-print-settings", settings);
}
