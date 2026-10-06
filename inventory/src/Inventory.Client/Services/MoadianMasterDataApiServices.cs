using Inventory.Shared.Dtos;

namespace Inventory.Client.Services;

/// <summary>Provider-scoped Moadian master-data API client and secure private-key management.</summary>
public interface IMoadianMasterDataClientService
{
    Task<List<MoadianServiceProviderProfileDto>> GetProvidersAsync();
    Task<MoadianServiceProviderProfileDto> CreateProviderAsync(MoadianServiceProviderProfileDto dto);
    Task<MoadianServiceProviderProfileDto> UpdateProviderAsync(int id, MoadianServiceProviderProfileDto dto);
    Task DeleteProviderAsync(int id);
    Task RestoreProviderAsync(int id);

    Task<List<MoadianProviderConnectionProfileDto>> GetConnectionsAsync(int? serviceProviderId = null);
    Task<MoadianProviderConnectionProfileDto> CreateConnectionAsync(MoadianProviderConnectionProfileDto dto);
    Task<MoadianProviderConnectionProfileDto> UpdateConnectionAsync(int id, MoadianProviderConnectionProfileDto dto);
    Task DeleteConnectionAsync(int id);
    Task RestoreConnectionAsync(int id);
    /// <summary>حذف قطعی اتصال همراه با فایل کلید خصوصی آن.</summary>
    Task DeleteConnectionPermanentlyAsync(int id);
    Task<MoadianPrivateKeyStatusDto> UploadPrivateKeyAsync(int id, Stream fileStream, string fileName, string? contentType = null);
    Task<MoadianPrivateKeyStatusDto> RemovePrivateKeyAsync(int id);
    Task<MoadianConnectionTestResultDto> TestConnectionAsync(int id);

    Task<List<MoadianCustomerProfileDto>> GetCustomersAsync(int? serviceProviderId = null);
    Task<MoadianCustomerProfileDto> CreateCustomerAsync(MoadianCustomerProfileDto dto);
    Task<MoadianCustomerProfileDto> UpdateCustomerAsync(int id, MoadianCustomerProfileDto dto);
    Task DeleteCustomerAsync(int id);
    Task RestoreCustomerAsync(int id);

    Task<List<MoadianGoodsOrServiceProfileDto>> GetGoodsOrServicesAsync(int? serviceProviderId = null);
    Task<MoadianGoodsOrServiceProfileDto> CreateGoodsOrServiceAsync(MoadianGoodsOrServiceProfileDto dto);
    Task<MoadianGoodsOrServiceProfileDto> UpdateGoodsOrServiceAsync(int id, MoadianGoodsOrServiceProfileDto dto);
    Task DeleteGoodsOrServiceAsync(int id);
    Task RestoreGoodsOrServiceAsync(int id);
    /// <summary>ورود گروهی «کالا و خدمت» از فایل اکسل (xlsx).</summary>
    Task<ExcelImportResult> ImportGoodsOrServicesAsync(Stream fileStream, string fileName);
    /// <summary>دانلود فایل اکسل نمونهٔ «کالا و خدمت».</summary>
    Task<(byte[] Data, string FileName, string ContentType)> DownloadGoodsOrServicesTemplateAsync();

    Task<List<MoadianUnitOfMeasurementDto>> GetUnitsAsync();
    Task<MoadianUnitOfMeasurementDto> CreateUnitAsync(MoadianUnitOfMeasurementDto dto);
    Task<MoadianUnitOfMeasurementDto> UpdateUnitAsync(int id, MoadianUnitOfMeasurementDto dto);
    Task DeleteUnitAsync(int id);
    Task RestoreUnitAsync(int id);
}

public sealed class MoadianMasterDataClientService : IMoadianMasterDataClientService
{
    private readonly IApiClient _api;
    public MoadianMasterDataClientService(IApiClient api) => _api = api;

    public Task<List<MoadianServiceProviderProfileDto>> GetProvidersAsync()
        => _api.GetAsync<List<MoadianServiceProviderProfileDto>>("api/moadian/master-data/providers");
    public Task<MoadianServiceProviderProfileDto> CreateProviderAsync(MoadianServiceProviderProfileDto dto)
        => _api.PostAsync<MoadianServiceProviderProfileDto>("api/moadian/master-data/providers", dto);
    public Task<MoadianServiceProviderProfileDto> UpdateProviderAsync(int id, MoadianServiceProviderProfileDto dto)
        => _api.PutAsync<MoadianServiceProviderProfileDto>($"api/moadian/master-data/providers/{id}", dto);
    public Task DeleteProviderAsync(int id) => _api.DeleteAsync($"api/moadian/master-data/providers/{id}");
    public Task RestoreProviderAsync(int id)
        => _api.PostAsync<object>($"api/moadian/master-data/providers/{id}/restore", new { });

    public Task<List<MoadianProviderConnectionProfileDto>> GetConnectionsAsync(int? serviceProviderId = null)
        => _api.GetAsync<List<MoadianProviderConnectionProfileDto>>(WithProvider("connections", serviceProviderId));
    public Task<MoadianProviderConnectionProfileDto> CreateConnectionAsync(MoadianProviderConnectionProfileDto dto)
        => _api.PostAsync<MoadianProviderConnectionProfileDto>("api/moadian/master-data/connections", dto);
    public Task<MoadianProviderConnectionProfileDto> UpdateConnectionAsync(int id, MoadianProviderConnectionProfileDto dto)
        => _api.PutAsync<MoadianProviderConnectionProfileDto>($"api/moadian/master-data/connections/{id}", dto);
    public Task DeleteConnectionAsync(int id) => _api.DeleteAsync($"api/moadian/master-data/connections/{id}");
    public Task DeleteConnectionPermanentlyAsync(int id)
        => _api.DeleteAsync($"api/moadian/master-data/connections/{id}/permanent");
    public Task RestoreConnectionAsync(int id)
        => _api.PostAsync<object>($"api/moadian/master-data/connections/{id}/restore", new { });
    public Task<MoadianPrivateKeyStatusDto> UploadPrivateKeyAsync(int id, Stream fileStream, string fileName, string? contentType = null)
        => _api.PostFileAsync<MoadianPrivateKeyStatusDto>($"api/moadian/master-data/connections/{id}/private-key", fileStream, fileName, contentType: contentType);
    public Task<MoadianPrivateKeyStatusDto> RemovePrivateKeyAsync(int id)
        => _api.DeleteAsync<MoadianPrivateKeyStatusDto>($"api/moadian/master-data/connections/{id}/private-key");
    public Task<MoadianConnectionTestResultDto> TestConnectionAsync(int id)
        => _api.PostAsync<MoadianConnectionTestResultDto>($"api/moadian/master-data/connections/{id}/test-connection", new { });

    public Task<List<MoadianCustomerProfileDto>> GetCustomersAsync(int? serviceProviderId = null)
        => _api.GetAsync<List<MoadianCustomerProfileDto>>(WithProvider("customers", serviceProviderId));
    public Task<MoadianCustomerProfileDto> CreateCustomerAsync(MoadianCustomerProfileDto dto)
        => _api.PostAsync<MoadianCustomerProfileDto>("api/moadian/master-data/customers", dto);
    public Task<MoadianCustomerProfileDto> UpdateCustomerAsync(int id, MoadianCustomerProfileDto dto)
        => _api.PutAsync<MoadianCustomerProfileDto>($"api/moadian/master-data/customers/{id}", dto);
    public Task DeleteCustomerAsync(int id) => _api.DeleteAsync($"api/moadian/master-data/customers/{id}");
    public Task RestoreCustomerAsync(int id)
        => _api.PostAsync<object>($"api/moadian/master-data/customers/{id}/restore", new { });

    public Task<List<MoadianGoodsOrServiceProfileDto>> GetGoodsOrServicesAsync(int? serviceProviderId = null)
        => _api.GetAsync<List<MoadianGoodsOrServiceProfileDto>>(WithProvider("goods-services", serviceProviderId));
    public Task<MoadianGoodsOrServiceProfileDto> CreateGoodsOrServiceAsync(MoadianGoodsOrServiceProfileDto dto)
        => _api.PostAsync<MoadianGoodsOrServiceProfileDto>("api/moadian/master-data/goods-services", dto);
    public Task<MoadianGoodsOrServiceProfileDto> UpdateGoodsOrServiceAsync(int id, MoadianGoodsOrServiceProfileDto dto)
        => _api.PutAsync<MoadianGoodsOrServiceProfileDto>($"api/moadian/master-data/goods-services/{id}", dto);
    public Task DeleteGoodsOrServiceAsync(int id) => _api.DeleteAsync($"api/moadian/master-data/goods-services/{id}");
    public Task<ExcelImportResult> ImportGoodsOrServicesAsync(Stream fileStream, string fileName)
        => _api.PostFileAsync<ExcelImportResult>("api/moadian/master-data/goods-services/import", fileStream, fileName);
    public Task<(byte[] Data, string FileName, string ContentType)> DownloadGoodsOrServicesTemplateAsync()
        => _api.GetFileAsync("api/moadian/master-data/goods-services/import/template");
    public Task RestoreGoodsOrServiceAsync(int id)
        => _api.PostAsync<object>($"api/moadian/master-data/goods-services/{id}/restore", new { });

    public Task<List<MoadianUnitOfMeasurementDto>> GetUnitsAsync()
        => _api.GetAsync<List<MoadianUnitOfMeasurementDto>>("api/moadian/master-data/units");
    public Task<MoadianUnitOfMeasurementDto> CreateUnitAsync(MoadianUnitOfMeasurementDto dto)
        => _api.PostAsync<MoadianUnitOfMeasurementDto>("api/moadian/master-data/units", dto);
    public Task<MoadianUnitOfMeasurementDto> UpdateUnitAsync(int id, MoadianUnitOfMeasurementDto dto)
        => _api.PutAsync<MoadianUnitOfMeasurementDto>($"api/moadian/master-data/units/{id}", dto);
    public Task DeleteUnitAsync(int id) => _api.DeleteAsync($"api/moadian/master-data/units/{id}");
    public Task RestoreUnitAsync(int id)
        => _api.PostAsync<object>($"api/moadian/master-data/units/{id}/restore", new { });

    private static string WithProvider(string resource, int? serviceProviderId)
        => serviceProviderId is null
            ? $"api/moadian/master-data/{resource}"
            : $"api/moadian/master-data/{resource}?serviceProviderId={serviceProviderId.Value}";
}
