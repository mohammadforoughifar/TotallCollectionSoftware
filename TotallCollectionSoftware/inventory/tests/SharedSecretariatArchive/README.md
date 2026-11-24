# Shared secretariat archive regression

This executable references the **production Inventory.Api project**. It uses the real AppDbContext model, EF Core SQLite provider, archive/incoming/outgoing/flow services, RBAC controller base and the actual incoming/outgoing MVC routes through an in-process TestServer. All users, letters, files and permissions are synthetic; no application settings, production SQL database, SMTP server or production startup are used.

```sh
dotnet run --project tests/SharedSecretariatArchive/SharedSecretariatArchive.csproj
```

The suite covers:

- Two authorized clerks sharing outgoing folders and letters, cross-user edit/move/unarchive, root moves, empty-folder deletion, cycles and cross-scope/invalid destinations.
- Invalid mixed batches rolling back completely, unsigned/deleted/wrong-kind letters, global duplicate prevention, old outgoing duplicate-link compatibility, original creator/actor IDs and archive counters.
- Incoming legacy registrar archives (`IncomingLetter.IsBayegani`) visible to all authorized clerks without importing any personal referral flag or personal folder title.
- Incoming shared operations leaving each recipient's `Erja.IsBayegani` and personal type 0/1 nodes untouched; personal unarchive/toggle/delete cannot modify either shared archive.
- Private list, reader and counter consistency, shared list search and SQL paging, deleted-source exclusion, folder tree paging, and separate outgoing/incoming storage types.
- HTTP 401 for anonymous requests and 403 for read-only/inactive-role users on every shared archive route; valid unified-secretariat, incoming-secretariat and legacy-admin access; immediate role revocation.
- Actual letter/attachment/circulation access for another clerk without impersonating an administrator or exposing private referral answers; the new read capability cannot be used to view an internal letter's flow.

The adjacent `tests/UnifiedSecretariatAvatars` suite also exercises the real Razor components and shared incoming folder picker, root selection, batch archive, explicit unarchive, move, folder creation/rename and realtime refresh. Its service doubles deliberately reject any accidental call from the unified incoming archive to a personal archive route. Existing incoming/outgoing avatar suites remain separate regressions.

## Constrained sandbox builds

The API is large. In the approximately 2 GiB sandbox, ordinary compilation exhausted memory. Build with a single process and workstation GC, then run the test project against that compiled API:

```sh
export DOTNET_PROCESSOR_COUNT=1 DOTNET_gcServer=0
export DOTNET_GCHeapHardLimit=0x50000000 DOTNET_GCConserveMemory=9

dotnet restore tests/SharedSecretariatArchive/SharedSecretariatArchive.csproj --disable-parallel -p:NuGetAudit=false
dotnet build src/Inventory.Api/Inventory.Api.csproj --no-restore -m:1 \
  -p:UseSharedCompilation=false -p:RunAnalyzers=false -p:EnableNETAnalyzers=false \
  -p:BuildInParallel=false -p:ConcurrentBuild=false -p:DebugType=None -p:DebugSymbols=false \
  -p:Optimize=true -p:Nullable=annotations -p:Features=run-nullable-analysis=never \
  -p:ProduceReferenceAssembly=false -p:ProduceOnlyReferenceAssembly=false

dotnet build tests/SharedSecretariatArchive/SharedSecretariatArchive.csproj --no-restore \
  -p:BuildProjectReferences=false -m:1 -p:UseSharedCompilation=false -p:RunAnalyzers=false

dotnet tests/SharedSecretariatArchive/bin/Debug/net8.0/SharedSecretariatArchive.dll
```

These are command-line build overrides, not repository changes. The suite does **not** replace default-CI/publish testing, production SQL Server concurrency testing or deployment verification. Serializable transactions protect shared archive writes and legacy materialization; concurrent SQL Server deadlocks/retry UX have not been validated against a live SQL Server in this sandbox.
