using Inventory.Api.Data;
using Inventory.Api.Services.HrCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class EmployeeFilterTests
{
    [Fact] public async Task Node_filter_precedes_count_and_paging_and_old_calls_remain_valid()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        db.HrMainOrgNodes.AddRange(new(){Id=1,Name="واحد اول",Code="A"},new(){Id=2,Name="واحد دوم",Code="B"});
        for(var i=1;i<=55;i++) db.HrEmployees.Add(new(){Id=i,Code=$"E{i:000}",FirstName="کاربر",LastName="آزمایش",NationalCode=$"{i:0000000000}",HrMainNodeId=i%2==0?2:1,HireDate=new DateTime(2026,1,1)});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new HrCoreService(db,null!,null!,null!);
        var (first,total)=await service.SearchEmployeesAsync(null,null,null,0,20,2);
        var (second,total2)=await service.SearchEmployeesAsync(null,null,null,20,20,2);
        Assert.Equal(27,total); Assert.Equal(total,total2); Assert.Equal(20,first.Count); Assert.Equal(7,second.Count);
        Assert.All(first.Concat(second),e=>Assert.Equal(2,e.HrMainNodeId));
        Assert.Equal(27,first.Concat(second).Select(e=>e.Id).Distinct().Count());
        var (old,oldTotal)=await service.SearchEmployeesAsync(null,null,null,0,20);
        Assert.Equal(55,oldTotal); Assert.Equal(20,old.Count);
        var (search,searchTotal)=await service.SearchEmployeesAsync("E004",null,0,0,20,2);
        Assert.Equal(1,searchTotal); Assert.Equal(4,Assert.Single(search).Id);
        var (missing,missingTotal)=await service.SearchEmployeesAsync(null,null,null,0,20,999);
        Assert.Empty(missing); Assert.Equal(0,missingTotal);
        var (negative,_)=await service.SearchEmployeesAsync(null,null,null,-10,0,2);
        Assert.Equal(2,Assert.Single(negative).Id);
        Assert.Empty(db.ChangeTracker.Entries());
    }
}
