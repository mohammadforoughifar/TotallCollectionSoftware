# Bale bulk link regression probe

Run `dotnet run --project inventory/tests/BaleBulkLink/BaleBulkLink.csproj` from the directory
containing `inventory/`. The probe needs .NET 8 and the repository's NuGet dependencies; it does
not touch the network (the Bale Safir service is replaced by an in-process fake HTTP handler).

It covers the whole "bulk-connect users to Bale" path:

- **Phone normalization** — `09…`, `+98…`, `98…`, `0098…`, spaced/dashed numbers and Persian
  (`۰۹۱۲…`) / Arabic-Indic (`٠٩١٢…`) digits all normalize to the `98XXXXXXXXXX` form that the
  official Safir service accepts; landlines, foreign numbers and wrong-length numbers are rejected.
- **Safir response parsing** — a successful response yields the message id (which proves the number
  has a Bale account); error code `17` is mapped to `NotBaleUser`, `8` to `InvalidPhone`, `3` to
  `RateLimited`, `20` to `PaymentRequired`, `21` to the contact cap, `4` to a server error;
  unparsable or empty responses are server errors, never a silent success.
- **Bulk result classification** — linked / already linked / no Bale account / invalid number /
  no number / failed are counted separately, and only active users take part.
- **Persistence** — the linked user's normalized `98…` number is stored on the user; no chat id is
  fabricated, because the bulk route is the phone route.
- **Idempotency** — re-running the operation links nobody new and sends no message to users who
  are already linked; a user without a Bale account *is* retried, because he may join Bale later.
  The per-user `request_id` is stable across runs, which is what makes Safir suppress the
  duplicate message.
- **Global failure handling** — running out of credit or hitting the rate limit aborts the run so
  the remaining users do not get a message either, and the reason is reported to the admin.
- **Missing credentials** — without an Api Access Key nothing is attempted at all and the admin is
  told exactly which setting is missing.
- **Authorization** — `POST api/notifications/bale/bulk-link` rejects anonymous callers (401),
  callers without a user id (401) and non-admin callers (403). It allows admins via the `Admin`
  role claim (200) **and** via the RBAC path — a user whose role carries the `Settings.Manage`
  permission (200) — while a role with only `Settings.View` is rejected (403).
- **Notification recipients** — a user who is linked to Bale *only* through the phone route
  (`BalePhoneLinked`) is a valid recipient for the automatic reports and notifications. This is
  the regression guard for a real gap: the AI services used to look at `BaleChatId` alone, so
  users who never had to link themselves were silently excluded from their own reports.
- **Schema repair** — `BaleLinkSchemaV1` adds the four new columns (`Users.BalePhone`,
  `Users.BalePhoneLinked`, `AppSettings.BaleSafirApiAccessKey`, `AppSettings.BaleSafirBotId`) to
  an *existing* SQLite database that predates the feature, is safe to run twice, and the new
  columns keep their values.

## How this project is built

The probe deliberately does **not** use a `ProjectReference` to `Inventory.Api`. That project
holds ~750 C# files, and a full compile of it needs more memory than a small CI box has (it is
killed with exit code 137 / OOM). Instead the probe compiles only the files it needs, by linking
the *real* sources into this project:

| What | Where it comes from |
| --- | --- |
| `BaleSafirService`, `BaleLinkService` | `src/Inventory.Api/Services/System/` — the real code under test |
| `BaleLinkSchemaV1` | `src/Inventory.Api/Data/` — the real schema repair under test |
| `MessengerLinkHelper` | `src/Inventory.Api/Services/Ai/` — the shared "is this user linked" rule |
| `NotificationsController` | `src/Inventory.Api/Controllers/System/` — the real endpoint under test |
| `AppDbContext` + all entities | `src/Inventory.Api/Data/`, `src/Inventory.Api/Entities/`, `src/Inventory.Shared/Entities/` — the real persistence model |
| `LightMessengerService.cs` | local: the `IMessengerService` contract only (the real implementation depends on the AI services) |
| `LightActiveCompanyMiddleware.cs` | local: the `ItemKey` constant only |

Because the linked files are compiled *into* this assembly, `internal` members such as
`BaleSafirService.ParseResponse` are reachable from the probe.

**If you change a contract, update the light stand-ins.** `LightMessengerService.cs` mirrors the
`IMessengerService` interface in `src/Inventory.Api/Services/System/MessengerService.cs`; if that
interface gains or loses a method, the probe stops compiling until the two agree.

The project also references `src/Inventory.Licensing/Inventory.Licensing.csproj`, because
`TehranClock` (used by the entities) depends on the licensing period model.

## Expected output

Every check prints `PASS: …`. The run ends with:

```
همهٔ بررسی‌های اتصال انبوهٔ بله با موفقیت گذشت.
```

Any failure throws an `Exception` with a `FAIL:` prefix and a non-zero exit code.

The current run is **85 checks**, all passing.

## Note after the merge with `origin/main`

The licensing work that landed on `origin/main` added the `RepairSms` entities, which reference
the `RepairSmsEventType` / `RepairSmsStatus` enums that live in
`src/Inventory.Shared/Dtos/RepairDtos.cs`. Because this probe compiles the *real* entity
folder, that DTO file has to be linked in as well — otherwise the probe stops compiling with
`CS0246: The type or namespace name 'RepairSmsEventType' could not be found`. The link is listed
in `BaleBulkLink.csproj` next to the other shared DTOs.

