#nullable enable
global using System;
global using System.Linq;
global using System.Collections.Generic;
global using System.Threading.Tasks;
using System.Linq.Expressions;
using System.Collections;

// These types intentionally fake EF, transactions, notifications, and MVC results. They do
// not model a database provider, query filters, ASP.NET authorization, or SignalR.
[AttributeUsage(AttributeTargets.Parameter)] public sealed class FromBodyAttribute : Attribute { }
public interface IActionResult { }
public sealed record Result(string Kind, object Value) : IActionResult;
public sealed class WorkOrder
{
    public int Id, OwnerUserId, Priority;
    public int? ParentOrderId, CompanyId, ProjectId;
    public DateTime DueAt;
    public string Status = "Open", Number = "", Title = "", OwnerName = "";
}
public static class WorkOrderPriority
{
    public const int High = 2, Urgent = 3;
    public static string ToFa(int priority) => "اولویت";
}
public sealed class FakeUser { public int Id; public string? FirstName, LastName; public string Username = ""; }
public sealed class FakeServerAlert { public string Category = ""; }
public sealed class FakeManagedServer { public string Name = ""; }
public sealed class FakeLetterSource
{
    public int Id, SourceType;
    public bool IsDelete;
    public FakeLetter? InnerLetter, OutgoingLetter, IncomingLetter;
}
public sealed class FakeLetter { public string? Title, LetterNumber, SadereNumber; }
public sealed class WorkOrderAssignee
{
    public int OrderId, UserId;
    public DateTime? RepliedAt, SeenAt;
    public bool? Done;
    public string? ReplyText, OwnerDecision;
}
public sealed class WorkOrderLog
{
    public int Id, OrderId;
    public string ActorName = "", Action = "";
    public string? Text, RequestKey, PayloadFingerprint;
}
public sealed class FakeLogger { public void LogWarning(string fmt, params object[] args) { } }
public sealed class FakeChangeTracker(FakeDb db) { public void Clear() { db.Dirty = false; db.Clears++; } }
public sealed class FakeDb
{
    public FakeSet<WorkOrder> WorkOrders = new();
    public FakeSet<WorkOrderAssignee> WorkOrderAssignees = new();
    public FakeSet<WorkOrderLog> WorkOrderLogs = new();
    public FakeSet<FakeLetterSource> LetterSources = new();
    public FakeSet<FakeUser> Users = new();
    public List<(int Order, int[] Recipients, Inventory.Shared.Dtos.WorkOrderSmsEventType Type)> Stages = new();
    public List<string> Events = new(), SnapshotAtCommit = new();
    public bool Dirty, FailSave, FailCommit;
    public int Clears, NextId;
    public FakeChangeTracker ChangeTracker => new(this);
    public FakeDatabase Database => new(this);
    public Task<int> SaveChangesAsync()
    {
        Events.Add("save");
        if (FailSave) { FailSave = false; throw new InvalidOperationException("fake database failure"); }
        foreach (var log in WorkOrderLogs.Items.Where(x => x.Id == 0)) log.Id = ++NextId;
        return Task.FromResult(1);
    }
}
public sealed class FakeDatabase(FakeDb db)
{
    public Task<FakeTx> BeginTransactionAsync(System.Data.IsolationLevel isolation)
    {
        if (isolation != System.Data.IsolationLevel.Serializable) throw new Exception("isolation");
        db.Events.Add("begin");
        return Task.FromResult(new FakeTx(db));
    }
}
public sealed class FakeTx : IAsyncDisposable
{
    readonly FakeDb db;
    readonly int oldLogs, oldStages;
    readonly List<(WorkOrderAssignee a, DateTime? reply, DateTime? seen, bool? done, string? text, string? decision)> originals;
    bool committed;
    public FakeTx(FakeDb d)
    {
        db = d; oldLogs = d.WorkOrderLogs.Items.Count; oldStages = d.Stages.Count;
        originals = d.WorkOrderAssignees.Items.Select(a => (a, a.RepliedAt, a.SeenAt, a.Done, a.ReplyText, a.OwnerDecision)).ToList();
    }
    public Task CommitAsync()
    {
        if (db.FailCommit) { db.FailCommit = false; throw new InvalidOperationException("fake commit failure"); }
        committed = true; db.Events.Add("commit");
        db.SnapshotAtCommit = db.WorkOrderLogs.Items.Select(l => l.OrderId + ":" + l.Action).ToList();
        return Task.CompletedTask;
    }
    public ValueTask DisposeAsync()
    {
        if (!committed)
        {
            db.WorkOrderLogs.Items.RemoveRange(oldLogs, db.WorkOrderLogs.Items.Count - oldLogs);
            db.Stages.RemoveRange(oldStages, db.Stages.Count - oldStages);
            foreach (var x in originals)
            {
                x.a.RepliedAt = x.reply; x.a.SeenAt = x.seen; x.a.Done = x.done;
                x.a.ReplyText = x.text; x.a.OwnerDecision = x.decision;
            }
            db.Events.Add("rollback");
        }
        return ValueTask.CompletedTask;
    }
}
public sealed class FakeSet<T> : IQueryable<T>
{
    public List<T> Items = new();
    public void Add(T item) => Items.Add(item);
    public Type ElementType => typeof(T);
    public Expression Expression => Items.AsQueryable().Expression;
    public IQueryProvider Provider => Items.AsQueryable().Provider;
    public IEnumerator<T> GetEnumerator() => Items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
namespace Microsoft.EntityFrameworkCore
{
    public static class Ext
    {
        public static bool FailNextProjectedQuery;
        public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> q) => q;
        public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> q, Expression<Func<T, bool>> p) => Task.FromResult(q.FirstOrDefault(p));
        public static Task<T?> FirstOrDefaultAsync<T>(this IQueryable<T> q)
        {
            if (FailNextProjectedQuery) { FailNextProjectedQuery = false; throw new InvalidOperationException("fake read failure"); }
            return Task.FromResult(q.FirstOrDefault());
        }
        public static Task<T?> SingleOrDefaultAsync<T>(this IQueryable<T> q) => Task.FromResult(q.SingleOrDefault());
    }
}
public sealed class FakeNotify(FakeDb db)
{
    public List<int> Attempts = new();
    public List<(int User, string Title, string Form)> Details = new();
    public List<string> Scopes = new();
    public HashSet<int> Fail = new();
    public HashSet<string> FailScopes = new();
    public Func<int, string, string, bool>? FailWhen;
    public int Broadcasts;
    public Task SendAsync(int user, string title, string body, string from, string form, string link)
    {
        if (db.Dirty) throw new Exception("previous tracked failed notification would be retried");
        Attempts.Add(user); Details.Add((user, title, form)); db.Events.Add("notify-" + user);
        if (Fail.Contains(user) || FailWhen?.Invoke(user, title, form) == true)
        {
            db.Dirty = true;
            throw new Exception("fake notification failure");
        }
        return Task.CompletedTask;
    }
    public Task BroadcastChangedAsync(string scope)
    {
        Broadcasts++; Scopes.Add(scope); db.Events.Add("broadcast-" + scope);
        if (FailScopes.Contains(scope)) throw new Exception("fake broadcast failure");
        return Task.CompletedTask;
    }
}
