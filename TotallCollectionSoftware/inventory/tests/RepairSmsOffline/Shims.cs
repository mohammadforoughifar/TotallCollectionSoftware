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

namespace Inventory.Shared { public enum RepairStatus { Received, InProgress, Ready, Delivered, Cancelled } }
namespace Inventory.Shared.Dtos {
 public enum RepairSmsEventType { Registration=1, Ready=2 }
 public enum RepairSmsStatus { Pending=1, Sending=2, Accepted=3, Delivered=4, Failed=5, Unknown=6, InvalidMobile=7, Disabled=8, NotConfigured=9, Cancelled=10 }
}
namespace Inventory.Shared.Entities {}
namespace Inventory.Api.Services {
 public enum KavenegarSmsSendState { Disabled, NotConfigured, InvalidRequest, Accepted, Delivered, Failed, Unknown }
 public sealed record KavenegarSmsSendResult(KavenegarSmsSendState State, long? MessageId=null, int? MessageStatus=null, int? ProviderCode=null);
 public interface IKavenegarSmsGateway { bool IsEnabled {get;} bool IsConfigured {get;} Task<KavenegarSmsSendResult> SendAsync(string recipient, string message); }
}
namespace Inventory.Api.Data {
 public class RepairOrder { public int Id {get;set;} public int? CompanyId {get;set;} public int PartyId {get;set;} public string Number {get;set;}=""; public Inventory.Shared.RepairStatus Status {get;set;} }
 public class Party { public int Id {get;set;} public int? CompanyId {get;set;} public string? Mobile {get;set;} }
 public class AppDbContext { public Microsoft.EntityFrameworkCore.DbSet<RepairSmsNotification> RepairSmsNotifications {get;}=new(); public Microsoft.EntityFrameworkCore.DbSet<RepairOrder> RepairOrders {get;}=new(); public Microsoft.EntityFrameworkCore.DbSet<Party> Parties {get;}=new(); }
}
namespace Microsoft.Extensions.Hosting { public abstract class BackgroundService { protected abstract Task ExecuteAsync(CancellationToken token); } }
namespace Microsoft.Extensions.Logging {
 public interface ILogger<T> { void LogWarning(string format, params object[] args); }
}
namespace Microsoft.Extensions.DependencyInjection {
 public interface IServiceScopeFactory { IServiceScope CreateScope(); }
 public interface IServiceScope : IDisposable { IServiceProvider ServiceProvider {get;} }
 public static class ServiceProviderServiceExtensions { public static T GetRequiredService<T>(this IServiceProvider p) => (T)p.GetService(typeof(T))!; }
}
namespace Microsoft.EntityFrameworkCore {
 public class DbSet<T> : IQueryable<T> {
    readonly List<T> items=new(); public void Add(T item)=>items.Add(item);
    public Type ElementType=>typeof(T); public Expression Expression=>items.AsQueryable().Expression;
    public IQueryProvider Provider=>items.AsQueryable().Provider;
    public IEnumerator<T> GetEnumerator()=>items.GetEnumerator(); IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
 }
 public class SetPropertyCalls<T> {
    public SetPropertyCalls<T> SetProperty<TP>(Expression<Func<T,TP>> selector, TP value)=>this;
 }
 public static class Extensions {
   public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> q)=>q;
   public static Task<bool> AnyAsync<T>(this IQueryable<T> q, Expression<Func<T,bool>> predicate, CancellationToken ct=default)=>Task.FromResult(q.Any(predicate));
   public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> q, CancellationToken ct=default)=>Task.FromResult(q.FirstOrDefault());
   public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> q, Expression<Func<T,bool>> predicate, CancellationToken ct=default)=>Task.FromResult(q.SingleOrDefault(predicate));
   public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> q, CancellationToken ct=default)=>Task.FromResult(q.SingleOrDefault());
   public static Task<int> ExecuteUpdateAsync<T>(this IQueryable<T> q, Expression<Func<SetPropertyCalls<T>,SetPropertyCalls<T>>> setters, CancellationToken ct=default) {
     var calls=new List<MethodCallExpression>(); Expression? e=setters.Body;
     while(e is MethodCallExpression c) { calls.Add(c); e=c.Object; }
     calls.Reverse(); var rows=q.ToList();
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
