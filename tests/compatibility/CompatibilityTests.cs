using System.Globalization;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using Inventory.Api.Controllers;
using Inventory.Client.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventory.Compatibility.Tests;

public class CompatibilityTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("fa-IR")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public void FixedStyle_is_invariant_and_keeps_optional_height(string culture)
    {
        using var scope = new CultureScope(culture);
        Assert.Equal("position:fixed;top:13px;left:24px;right:auto;width:301px;z-index:4500;max-width:calc(100vw - 16px);max-height:265px;",
            Popover.FixedStyle(12.6, 23.6, 300.6, 264.6));
        var plain = Popover.FixedStyle(10, 20, 300);
        Assert.DoesNotContain("max-height:", plain);
        Assert.Equal(plain, Popover.FixedStyle(10, 20, 300, -1));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fa-IR")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    public void CalendarStyle_preserves_viewport_limits_and_invariant_coordinates(string culture)
    {
        using var scope = new CultureScope(culture);
        Assert.Equal("position:fixed;top:max(8px,min(59px,calc(100vh - 340px)));left:max(8px,min(61px,calc(100vw - 300px)));right:auto;z-index:4500;max-width:calc(100vw - 16px);max-height:calc(100vh - 24px);overflow:auto;",
            Popover.CalendarStyle(100.6, 50.6));
    }

    public static IEnumerable<object[]> NetworkCases()
    {
        foreach (var method in new[] { "BuildFromCidr", "BuildTargets" })
        {
            yield return new object[] { method, "192.168.1.2", "255.255.255.252", 254, "192.168.1.1", "192.168.1.2", 2 };
            yield return new object[] { method, "10.20.30.12", "255.255.255.248", 254, "10.20.30.9", "10.20.30.14", 6 };
            // Values above Int32.MaxValue ensure signed integer conversions do not corrupt addresses.
            yield return new object[] { method, "250.200.150.130", "255.255.255.248", 254, "250.200.150.129", "250.200.150.134", 6 };
            yield return new object[] { method, "172.16.5.10", "255.255.0.0", 254, "172.16.5.1", "172.16.5.254", 254 };
            // Preserve existing /31 and /32 behavior: the local address only.
            yield return new object[] { method, "192.0.2.10", "255.255.255.254", 254, "192.0.2.10", "192.0.2.10", 1 };
            yield return new object[] { method, "192.0.2.10", "255.255.255.255", 254, "192.0.2.10", "192.0.2.10", 1 };
        }
    }

    [Theory]
    [MemberData(nameof(NetworkCases))]
    public void Target_builders_use_network_byte_order_without_scanning(
        string name, string ip, string mask, int maxCount, string first, string last, int count)
    {
        var type = name == "BuildFromCidr" ? typeof(NetworkScanController) : typeof(CctvScanController);
        var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
        var result = (List<string>)method.Invoke(null, new object[] { IPAddress.Parse(ip), IPAddress.Parse(mask), maxCount })!;
        Assert.Equal(count, result.Count);
        Assert.Equal(first, result[0]);
        Assert.Equal(last, result[^1]);
        Assert.Contains(ip, result);
        Assert.Equal(result.Count, result.Distinct().Count());
        Assert.All(result, address => Assert.Equal(System.Net.Sockets.AddressFamily.InterNetwork, IPAddress.Parse(address).AddressFamily));
    }

    [Fact]
    public void Captured_string_array_Contains_binds_to_Enumerable_not_ReadOnlySpan()
    {
        string[] roles = ["Admin", "Operator"];
        Expression<Func<string, bool>> expression = value => roles.Contains(value);
        var call = Assert.IsAssignableFrom<MethodCallExpression>(expression.Body);
        Assert.Equal(typeof(Enumerable), call.Method.DeclaringType);
        Assert.All(call.Arguments, argument => Assert.False(argument.Type.IsByRefLike));
        var predicate = expression.Compile(preferInterpretation: true);
        Assert.True(predicate("Admin"));
        Assert.False(predicate("Unknown"));
    }

    [Fact]
    public void Captured_constant_array_Contains_can_be_interpreted()
    {
        string[] roles = ["Admin", "Operator"];
        Expression<Func<bool>> expression = () => roles.Contains("Admin");
        Assert.True(expression.Compile(preferInterpretation: true)());
    }

    [Fact]
    public async Task EfCore_translates_array_membership_and_evaluates_captured_membership()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ProbeDb(new DbContextOptionsBuilder<ProbeDb>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Rows.AddRange(new ProbeRow { Name = "Admin" }, new ProbeRow { Name = "Guest" });
        await db.SaveChangesAsync();
        string[] names = ["Admin", "Operator"];
        var result = await db.Rows.Where(row => names.Contains(row.Name) && names.Contains("Admin")).ToListAsync();
        Assert.Equal("Admin", Assert.Single(result).Name);
    }

    private sealed class ProbeDb(DbContextOptions<ProbeDb> options) : DbContext(options)
    {
        public DbSet<ProbeRow> Rows => Set<ProbeRow>();
    }

    private sealed class ProbeRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _original = CultureInfo.CurrentCulture;
        public CultureScope(string culture) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        public void Dispose() => CultureInfo.CurrentCulture = _original;
    }
}
