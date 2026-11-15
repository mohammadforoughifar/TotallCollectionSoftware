using System.Security.Cryptography;
using System.Text;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Services.Remote;

public interface IRemoteAuthService
{
    Task<(RemoteDevice Device, string Key)> CreateDeviceAsync(string name, string companyCode);
    Task<string?> RotateKeyAsync(string deviceId);
    Task<RemoteDevice?> AuthenticateDeviceAsync(string? deviceKey);
    string HashKey(string key);
}

public sealed class RemoteAuthService : IRemoteAuthService
{
    private readonly AppDbContext _db;
    public RemoteAuthService(AppDbContext db) => _db = db;

    public string HashKey(string key)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim()))).ToUpperInvariant();

    private static string NewKey()
        => "rmd_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    public async Task<(RemoteDevice, string)> CreateDeviceAsync(string name, string companyCode)
    {
        var key = NewKey();
        var device = new RemoteDevice
        {
            Name = name.Trim(),
            CompanyCode = companyCode.Trim().ToUpperInvariant(),
            DeviceKeyHash = HashKey(key),
            DeviceKeyHint = key[..Math.Min(12, key.Length)],
        };
        _db.RemoteDevices.Add(device);
        await _db.SaveChangesAsync();
        return (device, key);
    }

    public async Task<string?> RotateKeyAsync(string deviceId)
    {
        var d = await _db.RemoteDevices.FirstOrDefaultAsync(x => x.Id == deviceId);
        if (d == null) return null;
        var key = NewKey();
        d.DeviceKeyHash = HashKey(key);
        d.DeviceKeyHint = key[..Math.Min(12, key.Length)];
        d.UpdatedAt = DateTime.Now;
        d.IsOnline = false;
        await _db.SaveChangesAsync();
        return key;
    }

    public async Task<RemoteDevice?> AuthenticateDeviceAsync(string? deviceKey)
    {
        if (string.IsNullOrWhiteSpace(deviceKey)) return null;
        var hash = HashKey(deviceKey);
        var candidates = await _db.RemoteDevices.AsNoTracking().Where(x => x.IsEnabled).ToListAsync();
        foreach (var d in candidates)
        {
            if (d.DeviceKeyHash.Length != hash.Length) continue;
            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(d.DeviceKeyHash), Encoding.UTF8.GetBytes(hash)))
                return d;
        }
        return null;
    }
}
