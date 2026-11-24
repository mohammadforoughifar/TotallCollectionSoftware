using System.Reflection;
using Inventory.Shared.Dtos;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnifiedSecretariatAvatarTests;

// These tests compile the identity expressions taken from the production API source.
// Queries execute over synthetic LINQ data, NOT a real EF provider/database or the full API.
public static class ServerProjectionTests
{
    public static void Run()
    {
        var checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("FAIL projection: " + message);
            Console.WriteLine("PASS projection: " + message); checks++;
        }
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "src/Inventory.Api"))) root = root.Parent;
        if (root == null) throw new DirectoryNotFoundException("Run inside the inventory source tree.");
        SyntaxNode Read(string path)
        {
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, path)));
            Check(!tree.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error), path + ": C# syntax is valid");
            return tree.GetRoot();
        }
        var incoming = Read("src/Inventory.Api/Services/Office/IncomingLetterService.cs");
        var outgoing = Read("src/Inventory.Api/Services/Office/Outgoing/OutgoingLetterService.cs");
        var archive = Read("src/Inventory.Api/Services/Office/ArchiveService.cs");
        var sharedArchive = Read("src/Inventory.Api/Services/Office/ArchiveService.Secretariat.cs");
        static MethodDeclarationSyntax Method(SyntaxNode node, string name) => node.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.ValueText == name && m.Body != null);
        static IEnumerable<ObjectCreationExpressionSyntax> Objects(SyntaxNode node, string dto) => node.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Where(n => n.Type.ToString() == dto);
        static string Expr(ObjectCreationExpressionSyntax node, string field) => node.Initializer!.Expressions.OfType<AssignmentExpressionSyntax>().Single(a => a.Left.ToString() == field).Right.ToString();
        var sent = Method(incoming, "GetSentAsync");
        var archived = Method(incoming, "GetArchiveAsync");
        foreach (var pair in new[] { ("registered", sent), ("direct archive", archived), ("shared secretariat", Method(sharedArchive, "GetIncomingSecretariatAsync")) })
        {
            var node = Objects(pair.Item2, nameof(IncomingLetterListItemDto)).Single(n => Expr(n, "SenderUserId") == "l.CreateUserId");
            var project = Compile<Func<IncomingSeed, UserContext, IncomingLetterListItemDto>>(
                "IncomingLetterListItemDto", "IncomingSeed l, UserContext _db",
                $"SenderUserId = {Expr(node, "SenderUserId")}, Sender = {Expr(node, "Sender")}, Ferestande = {Expr(node, "Ferestande")}");
            var users = new UserContext(new[]
            {
                new UserSeed(21, "first", "سارا", "الف"), new UserSeed(22, "second", "سارا", "الف"),
                new UserSeed(23, "login-only", null, "  ")
            });
            var result = project(new(21, Fixture.Outside), users);
            Check(result.SenderUserId == 21 && result.Sender == "سارا الف" && result.Ferestande == Fixture.Outside, pair.Item1 + ": real projection keeps registrar ID/name separate from external sender");
            result = project(new(22, Fixture.Outside), users);
            Check(result.SenderUserId == 22 && result.Sender == "سارا الف", pair.Item1 + ": same names keep distinct stored user IDs");
            result = project(new(23, Fixture.Outside), users);
            Check(result.Sender == "login-only", pair.Item1 + ": absent/whitespace personal names fall back to that user's login name");
            result = project(new(999, "سارا الف"), users);
            Check(result.SenderUserId == 999 && result.Sender == "", pair.Item1 + ": missing user is never resolved from matching external/display text");
            Check(!pair.Item2.ToString().Contains("SaveChanges") && !pair.Item2.ToString().Contains("GetDetailAsync"), pair.Item1 + ": identity projection performs no read-state save or detail retrieval");
        }
        Check(sent.ToString().Contains("l.CreateUserId == userId") && archived.ToString().Contains("e.ReciverUserId == userId") && archived.ToString().Contains("b.UserId == userId"),
            "incoming registered/archive access scopes remain user-bound");
        var inboxNode = Objects(Method(incoming, "GetInboxAsync"), nameof(IncomingLetterListItemDto)).Single();
        Check(Expr(inboxNode, "SenderUserId") == "e.SenderUserId", "received inbox still uses the referral's sender, never the original registrar");

        var secretariat = Method(outgoing, "GetDabirkhaneAsync");
        var listNode = Objects(secretariat, nameof(DabirkhaneListItemDto)).Single();
        var listProject = Compile<Func<OutgoingSeed, DabirkhaneListItemDto>>("DabirkhaneListItemDto", "OutgoingSeed l",
            $"CreatorUserId = {Expr(listNode, "CreatorUserId")}, DabirkhaneUserId = {Expr(listNode, "DabirkhaneUserId")}");
        var row = listProject(new(11, 51));
        Check(row.CreatorUserId == 11 && row.DabirkhaneUserId == 51, "outgoing list projects two independent stored identities: creator and registering clerk");
        Check(listProject(new(11, null)).DabirkhaneUserId == null, "unknown registration identity is not substituted with the creator");
        var detailNode = Objects(Method(outgoing, "GetDetailAsync"), nameof(OutgoingLetterDetailDto)).Single();
        var detailProject = Compile<Func<OutgoingSeed, OutgoingLetterDetailDto>>("OutgoingLetterDetailDto", "OutgoingSeed letter",
            $"SenderUserId = {Expr(detailNode, "SenderUserId")}, DabirkhaneUserId = {Expr(detailNode, "DabirkhaneUserId")}");
        var detail = detailProject(new(12, 52));
        Check(detail.SenderUserId == 12 && detail.DabirkhaneUserId == 52, "outgoing detail returns the same authoritative registering-user ID");
        Check(!secretariat.ToString().Contains("SaveChanges") && secretariat.ToString().Contains("ToPageListAsync(pagination)"), "outgoing list preserves paged reads without workflow writes");
        var tree = Method(archive, "GetOutgoingTreeAsync");
        var treeNode = Objects(tree, nameof(BayeganiNodeDto)).Single();
        var archiveProject = Compile<Func<OutgoingSeed, BayeganiNodeDto>>("BayeganiNodeDto", "OutgoingSeed l", $"CreatorUserId = {Expr(treeNode, "CreatorUserId")}");
        Check(archiveProject(new(13, 53)).CreatorUserId == 13 && tree.ToString().Contains("n.CreatorUserId = li.CreatorUserId;"), "archive carries the letter creator ID through final tree-node hydration");
        Check(!tree.ToString().Contains("b.UserId == userId") && tree.ToString().Contains("SharedArchiveRows(TypeOutgoing)") && tree.ToString().Contains("ReadTreePageAsync") && !tree.ToString().Contains("SaveChanges"), "shared archive has no per-user ownership filter; type scope, tree paging and read-only behavior stay intact");

        var old = System.Text.Json.JsonSerializer.Deserialize<DabirkhaneListItemDto>("{\"CreatorUserId\":11,\"DabirkhaneUserName\":\"same name\"}")!;
        Check(old.DabirkhaneUserId == null, "old API JSON without the new optional ID safely yields a default avatar");
        var roundtrip = System.Text.Json.JsonSerializer.Deserialize<DabirkhaneListItemDto>(System.Text.Json.JsonSerializer.Serialize(row))!;
        Check(roundtrip.CreatorUserId == 11 && roundtrip.DabirkhaneUserId == 51, "new optional identity metadata survives JSON roundtrip without conflating roles");
        Console.WriteLine($"ALL {checks} SECRETARIAT SOURCE/PROJECTION CHECKS PASSED (synthetic LINQ; not EF/SQL integration).");
    }
    private static T Compile<T>(string result, string args, string assignments) where T : Delegate
    {
        var source = $$"""
            using System;
            using System.Linq;
            using Inventory.Shared.Dtos;
            using UnifiedSecretariatAvatarTests;
            public static class IdentityProjection {
                public static {{result}} Project({{args}}) => new {{result}} { {{assignments}} };
            }
            """;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(IncomingLetterListItemDto).Assembly.Location).Append(typeof(IncomingSeed).Assembly.Location)
            .Distinct().Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("IdentityProjection" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics));
        return (T)Assembly.Load(stream.ToArray()).GetType("IdentityProjection")!.GetMethod("Project")!.CreateDelegate(typeof(T));
    }
}
public sealed record IncomingSeed(int CreateUserId, string Ferestande);
public sealed record OutgoingSeed(int CreatorUserId, int? DabirkhaneUserId);
public sealed record UserSeed(int Id, string Username, string? FirstName, string? LastName);
public sealed class UserContext(IEnumerable<UserSeed> users) { public IQueryable<UserSeed> Users => users.AsQueryable(); }
public static class NoTrackingForSyntheticLinq
{
    public static IQueryable<T> AsNoTracking<T>(this IQueryable<T> query) => query;
}
