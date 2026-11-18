global using System;
global using System.Linq;
global using System.Collections.Generic;
global using System.Threading;
global using System.Threading.Tasks;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace Inventory.Api.Services {
 public enum KavenegarSmsSendState { Disabled, NotConfigured, InvalidRequest, Accepted, Delivered, Failed, Unknown }
 public sealed record KavenegarSmsSendResult(KavenegarSmsSendState State,long? MessageId=null,int? MessageStatus=null,int? ProviderCode=null);
 public interface IKavenegarSmsGateway {
  bool IsEnabled {get;} bool IsConfigured {get;} bool IsLookupConfigured {get;}
  Task<KavenegarSmsSendResult> SendAsync(string recipient,string message);
  Task<KavenegarSmsSendResult> SendLookupAsync(string recipient,string token,string name,string template);
  Task<KavenegarSmsSendResult> SendLookupWithToken20Async(string recipient,string token,string company,string token20,string template);
  Task<KavenegarSmsSendResult> SendWorkOrderLookupAsync(string recipient,string year,string serial,string company,string title,string? deadline,string template);
 }
}
namespace Inventory.Api.Data {
 public sealed class SystemCompany {public int Id {get;set;} public string Name {get;set;}=""; public bool IsActive {get;set;}=true;}
 public sealed class User {public int Id {get;set;} public string Username {get;set;}=""; public string Role {get;set;}="Operator"; public string? Mobile {get;set;} public bool IsActive {get;set;}=true;}
 public sealed class SystemUser {public string Username {get;set;}=""; public int? CompanyId {get;set;}}
 public sealed class UserCompanyAccess {public int UserId {get;set;} public int CompanyId {get;set;} public bool IsDefault {get;set;} public SystemCompany Company {get;set;}=null!;}
 public sealed class WorkOrder {public int Id {get;set;} public int? CompanyId {get;set;} public int? ParentOrderId {get;set;} public string Number {get;set;}=""; public string Title {get;set;}=""; public int OwnerUserId {get;set;} public DateTime DueAt {get;set;} public int Recurrence {get;set;} public string? RecurrenceSeriesId {get;set;}}
 public sealed class WorkOrderLog {public int Id {get;set;} public int OrderId {get;set;} public string Action {get;set;}="";}
 public sealed class WorkOrderAssignee {public int OrderId {get;set;} public int UserId {get;set;}}
 public sealed class AppDbContext {
  public int? ActiveCompanyId {get;set;}=1;
  public bool FailNextSave {get;set;}
  public Microsoft.EntityFrameworkCore.DbSet<SystemCompany> SystemCompanies {get;}=new();
  public Microsoft.EntityFrameworkCore.DbSet<User> Users {get;}=new();
  public Microsoft.EntityFrameworkCore.DbSet<SystemUser> SystemUsers {get;}=new();
  public Microsoft.EntityFrameworkCore.DbSet<UserCompanyAccess> UserCompanyAccesses {get;}=new();
  public Microsoft.EntityFrameworkCore.DbSet<CompanySmsSetting> CompanySmsSettings {get;}=new();
  public Microsoft.EntityFrameworkCore.DbSet<WorkOrder> WorkOrders {get;}=new();
  public Microsoft.EntityFrameworkCore.DbSet<WorkOrderLog> WorkOrderLogs {get;}=new();
  public Microsoft.EntityFrameworkCore.DbSet<WorkOrderAssignee> WorkOrderAssignees {get;}=new();
  public Microsoft.EntityFrameworkCore.DbSet<WorkOrderSmsNotification> WorkOrderSmsNotifications {get;}=new();
  public FakeChangeTracker ChangeTracker => new(this);
  public Task<int> SaveChangesAsync(CancellationToken ct=default) {
   if(FailNextSave) { FailNextSave=false; throw new InvalidOperationException("fake staging failure"); }
   return Task.FromResult(1);
  }
 }
 public sealed class FakeChangeTracker(AppDbContext db) {
  public IEnumerable<Microsoft.EntityFrameworkCore.FakeEntry<T>> Entries<T>() where T:class {
   if(typeof(T)!=typeof(WorkOrderSmsNotification)) return Array.Empty<Microsoft.EntityFrameworkCore.FakeEntry<T>>();
   return db.WorkOrderSmsNotifications.Local.Select(row => new Microsoft.EntityFrameworkCore.FakeEntry<T>((T)(object)row,
      ()=>db.WorkOrderSmsNotifications.Local.Remove(row))).ToArray();
  }
 }
 public sealed class FakeTransaction(AppDbContext db) : Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction {
  public bool SupportsSavepoints {get;set;}=true; public bool Created, RolledBack, FailRollback; int count;
  public Task CreateSavepointAsync(string name,CancellationToken ct=default) {Created=true;count=db.WorkOrderSmsNotifications.Count();return Task.CompletedTask;}
  public Task RollbackToSavepointAsync(string name,CancellationToken ct=default) {
   RolledBack=true; if(FailRollback) throw new InvalidOperationException("fake rollback failure");
   while(db.WorkOrderSmsNotifications.Count()>count) db.WorkOrderSmsNotifications.Local.RemoveAt(db.WorkOrderSmsNotifications.Count()-1);
   return Task.CompletedTask;
  }
 }
}
namespace Microsoft.EntityFrameworkCore.Storage {
 public interface IDbContextTransaction {
  bool SupportsSavepoints {get;}
  Task CreateSavepointAsync(string name,CancellationToken cancellationToken=default);
  Task RollbackToSavepointAsync(string name,CancellationToken cancellationToken=default);
 }
}
namespace Microsoft.EntityFrameworkCore {
 public enum EntityState { Detached, Added, Unchanged }
 public sealed class FakeEntry<T>(T entity,Action detach) {
  public T Entity {get;}=entity; EntityState state=EntityState.Added;
  public EntityState State {get=>state;set {state=value;if(value==EntityState.Detached) detach();}}
 }
 public class DbSet<T> : IQueryable<T> {
  readonly List<T> items=new(); public List<T> Local=>items; public void Add(T item)=>items.Add(item);
  public Type ElementType=>typeof(T); public Expression Expression=>items.AsQueryable().Expression;
  public IQueryProvider Provider=>items.AsQueryable().Provider;
  public IEnumerator<T> GetEnumerator()=>items.GetEnumerator(); IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
 }
 public class SetPropertyCalls<T> {public SetPropertyCalls<T> SetProperty<TP>(Expression<Func<T,TP>> selector,TP value)=>this;}
 public static class Extensions {
  public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> q)=>q;
  public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> q)=>q;
  public static Task<bool> AnyAsync<T>(this IQueryable<T> q,Expression<Func<T,bool>> p,CancellationToken ct=default)=>Task.FromResult(q.Any(p));
  public static Task<List<T>> ToListAsync<T>(this IQueryable<T> q,CancellationToken ct=default)=>Task.FromResult(q.ToList());
  public static Task<int> CountAsync<T>(this IQueryable<T> q,CancellationToken ct=default)=>Task.FromResult(q.Count());
  public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> q,CancellationToken ct=default)=>Task.FromResult(q.FirstOrDefault());
  public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> q,CancellationToken ct=default)=>Task.FromResult(q.SingleOrDefault());
  public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> q,Expression<Func<T,bool>> p,CancellationToken ct=default)=>Task.FromResult(q.SingleOrDefault(p));
  public static Task<int> ExecuteUpdateAsync<T>(this IQueryable<T> q,Expression<Func<SetPropertyCalls<T>,SetPropertyCalls<T>>> setters,CancellationToken ct=default) {
   var calls=new List<MethodCallExpression>();Expression? e=setters.Body;
   while(e is MethodCallExpression c) {calls.Add(c);e=c.Object;} calls.Reverse();var rows=q.ToList();
   foreach(var call in calls) {
    var selector=(LambdaExpression)((UnaryExpression)call.Arguments[0]).Operand;
    var prop=(PropertyInfo)((MemberExpression)selector.Body).Member;
    var val=Expression.Lambda(Expression.Convert(call.Arguments[1],typeof(object))).Compile().DynamicInvoke();
    foreach(var row in rows) prop.SetValue(row,val);
   }
   return Task.FromResult(rows.Count);
  }
 }
}
namespace Microsoft.Extensions.Options {
 public interface IOptions<out T> {T Value {get;}}
 public sealed class OptionsWrapper<T>(T value):IOptions<T> {public T Value {get;}=value;}
}
namespace Microsoft.Extensions.Logging {
 public interface ILogger<T> {void LogWarning(string format,params object[] args);}
 public static class LoggerExtensions {
  public static void LogWarning<T>(this ILogger<T> logger,Exception exception,string format,params object[] args)=>logger.LogWarning(format,args);
 }
}
namespace Microsoft.Extensions.Hosting {public abstract class BackgroundService {protected abstract Task ExecuteAsync(CancellationToken ct);}}
namespace Microsoft.Extensions.DependencyInjection {
 public interface IServiceScope:IDisposable {IServiceProvider ServiceProvider {get;}}
 public interface IServiceScopeFactory {IServiceScope CreateScope();}
 public static class Extensions {public static T GetRequiredService<T>(this IServiceProvider provider)=>(T)provider.GetService(typeof(T))!;}
}

// Enough HTTP shape to execute the real active-company middleware in this offline fixture.
// This is not ASP.NET middleware hosting or a real EF tenant-query-filter test.
public delegate Task RequestDelegate(HttpContext context);
public sealed class HttpContext {
 public FakeRequest Request {get;}=new();
 public System.Security.Claims.ClaimsPrincipal User {get;set;}=new();
 public Dictionary<object,object?> Items {get;}=new();
}
public sealed class FakeRequest {
 public FakePath Path {get;set;}=new("/api/servers/alerts/1/work-order");
 public FakeHeaders Headers {get;}=new();
}
public sealed class FakePath(string value) {
 public bool StartsWithSegments(string prefix,StringComparison comparison)=>
  value.StartsWith(prefix,comparison) && (value.Length==prefix.Length || value[prefix.Length]=='/');
 public override string ToString()=>value;
}
public sealed class FakeHeaders {
 readonly Dictionary<string,string[]> values=new(StringComparer.OrdinalIgnoreCase);
 public string[] this[string key] {get=>values.TryGetValue(key,out var value)?value:Array.Empty<string>();set=>values[key]=value;}
}
namespace System.Security.Claims {
 public static class FakeClaimsExtensions {
  public static string? FindFirstValue(this ClaimsPrincipal principal,string type)=>principal.FindFirst(type)?.Value;
 }
}
