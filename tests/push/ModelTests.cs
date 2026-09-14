using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
namespace Inventory.Push.Tests;
public sealed class ModelTests
{
    [Fact] public void Push_tables_snapshot_matches_model()
    {
        using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;TrustServerCertificate=True").Options);
        var snapshot=db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        var source=db.GetService<IModelRuntimeInitializer>().Initialize(snapshot,designTime:true);
        var current=db.GetService<IDesignTimeModel>().Model;
        var diffs=db.GetService<IMigrationsModelDiffer>().GetDifferences(source.GetRelationalModel(),current.GetRelationalModel());
        // Other modules use older bootstrap schemas; the existing chat migration test reports
        // their global drift. This regression is deliberately scoped to the new push schema.
        diffs = diffs.Where(d => {
            var table = d.GetType().GetProperty("Table")?.GetValue(d) as string;
            var name = d.GetType().GetProperty("Name")?.GetValue(d) as string;
            return (table?.StartsWith("Push") ?? false) || (name?.StartsWith("Push") ?? false);
        }).ToArray();
        Assert.True(diffs.Count==0,string.Join("\n",diffs.Select(d=>d.GetType().Name+" "+string.Join(", ",d.GetType().GetProperties().Where(p=>p.PropertyType==typeof(string)||p.PropertyType==typeof(bool)||p.PropertyType==typeof(Type)).Select(p=>p.Name+"="+p.GetValue(d))))));
    }
}
