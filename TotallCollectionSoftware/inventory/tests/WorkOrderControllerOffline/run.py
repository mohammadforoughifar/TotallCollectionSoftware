#!/usr/bin/env python3
"""Run selected production controller code with fakes, without sending any messages.

This is intentionally NOT an ASP.NET/EF integration test. The actual Reply action,
notification helpers, server-alert title guard, and post-commit tails of
Create/Update/AddAssignees/server-alert creation are extracted from the current sources
before compilation. Transaction, query, logger, and notification implementations are
controlled in-memory fakes.

With a .NET 8 SDK: python tests/WorkOrderControllerOffline/run.py
With the optional Playpad WASM runner: python tests/WorkOrderControllerOffline/run.py --wasm-runner /path/to/run-wasm.mjs
"""
from __future__ import annotations

import argparse
import html
from pathlib import Path
import shutil
import subprocess
import tempfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
controller = (ROOT / 'src/Inventory.Api/Controllers/ItAssets/WorkOrdersController.cs').read_text(encoding='utf-8')
server = (ROOT / 'src/Inventory.Api/Controllers/ItAssets/ServersController.cs').read_text(encoding='utf-8')


def block(start: str) -> str:
    start_index = controller.index(start)
    end_index = controller.index('\n    }', start_index) + len('\n    }')
    return controller[start_index:end_index]


def expression(start: str) -> str:
    start_index = controller.index(start)
    return controller[start_index:controller.index(';', start_index) + 1]


def after_commit(source: str, signature: str, commit: str) -> str:
    """Extract one method's actual notification tail, excluding its closing brace."""
    start = source.index(signature)
    end = source.index('\n    }', start)
    section = source[start:end]
    start = section.index('var notificationsOk =', section.rindex(commit))
    return section[start:]


prefix = '''#nullable enable
using Microsoft.EntityFrameworkCore;
using Inventory.Api.Services.ItAssets;
using Inventory.Shared.Dtos;
public partial class ReplyHarness {
 private readonly FakeDb _db; private readonly FakeNotify _notify; private readonly FakeLogger _logger=new();
 public int MyUserId=3; public string MyUsername="respondent"; public bool StageResult=true, StageThrows;
 public ReplyHarness(FakeDb db, FakeNotify notify) { _db=db; _notify=notify; }
 private IActionResult Ok(object value)=>new Result("ok",value);
 private IActionResult NotFound(object value)=>new Result("notfound",value);
 private IActionResult BadRequest(object value)=>new Result("badrequest",value);
 private IActionResult ChangedRequestConflict()=>new Result("conflict",new {});
 private Task<List<int>> UsersWithWorkOrderViewAsync()=>Task.FromResult(new List<int>());
 private Task<string> AssigneesFaAsync(int id)=>Task.FromResult("کاربر");
 private static string ToFa(DateTime due)=>due.ToString("O");
 private Task<bool> TryStageSmsAsync(FakeTx tx, WorkOrder order, WorkOrderLog log, IReadOnlyCollection<int> users, WorkOrderSmsEventType type) {
  _db.Events.Add("stage"); _db.Stages.Add((order.Id,users.ToArray(),type));
  if(StageThrows) throw new InvalidOperationException("fake unrecoverable staging failure");
  return Task.FromResult(StageResult);
 }
'''
parts = [
    expression('private static string RequestKey('),
    expression('private static string Fingerprint<T>('),
    block('private async Task<(int OrderId, string? Fingerprint)?> FindRequestAsync('),
    block('private WorkOrderLog Log('),
    block('private async Task<bool> TryAfterCommitAsync('),
    controller[controller.index('private sealed record DeferredLetterNotice('):].splitlines()[0],
    block('private async Task<bool> NotifyNewLetterAccessAsync('),
    block('private async Task<bool> NotifyDeferredLetterAccessAsync('),
    controller[controller.index('public class ReplyDto {'):].splitlines()[0],
    block('public async Task<IActionResult> Reply('),
    block('private async Task<List<WorkOrder>> LoadAncestorsAsync('),
    block('private async Task<bool> NotifyReplyOwnersAsync('),
]

# Each wrapper supplies locals from immediately before the real post-commit section.
# These tails run unchanged, so a source change is picked up on the very next test run.
create_wrapper = '''public async Task<IActionResult> CreateNotices(WorkOrder wo, WorkOrder? parent, List<FakeUser> users,
 int added, bool hasProject, int letterFlowAdded, bool smsStaged, bool twoLetters=false) {
 var letterNotices=Notices(letterFlowAdded, twoLetters); object? project=hasProject ? new object() : null;
'''
update_wrapper = '''public async Task<IActionResult> UpdateNotices(WorkOrder wo, List<int> addedUserIds, List<int> removedUserIds,
 int added, int? oldProjectId, int letterFlowAdded, bool smsStaged) {
 var letterNotices=Notices(letterFlowAdded, false);
'''
assign_wrapper = '''public async Task<IActionResult> AssignNotices(WorkOrder wo, List<FakeUser> users, int skipped,
 string? note, int letterFlowAdded, bool smsStaged) {
 var letterNotices=Notices(letterFlowAdded, false); var dup=Enumerable.Range(0,skipped).ToList();
'''
notices = '''private static List<DeferredLetterNotice> Notices(int count, bool two) {
 var result=new List<DeferredLetterNotice>();
 if(count>0) result.Add(new DeferredLetterNotice(1,new List<int>{4,5},"letter title","WO/1405/2",3));
 if(two) result.Add(new DeferredLetterNotice(2,new List<int>{6},"another title","WO/1405/2",3));
 return result;
}
'''
parts.extend([
    notices,
    create_wrapper + after_commit(controller, 'public async Task<IActionResult> Create(', 'tx.CommitAsync()') + '\n}',
    update_wrapper + after_commit(controller, 'public async Task<IActionResult> Update(', 'tx.CommitAsync()') + '\n}',
    assign_wrapper + after_commit(controller, 'public async Task<IActionResult> AddAssignees(', 'tx.CommitAsync()') + '\n}',
])
server_prefix = '''public partial class ServerHarness {
 private readonly FakeDb _db; private readonly FakeNotify _notify; private readonly FakeLogger _logger=new();
 private readonly int UserId=3;
 public ServerHarness(FakeDb db, FakeNotify notify) { _db=db; _notify=notify; }
 private IActionResult Ok(object value)=>new Result("ok",value);
 private IActionResult BadRequest(object value)=>new Result("badrequest",value);
 private Task<string> MyNameAsync()=>Task.FromResult("مالک");
 public async Task<IActionResult> TitleGuard(FakeServerAlert alert, FakeManagedServer server) {
'''
server_title_start = server.index('        var category =', server.index('public async Task<IActionResult> CreateWorkOrderFromAlert('))
server_title_end = server.index('        var wo = new WorkOrder', server_title_start)
server_title = server[server_title_start:server_title_end]
source = (prefix + '\n\n'.join(parts) + '\n}\n' + server_prefix + server_title +
          '\nreturn Ok(new { title });\n}\n' +
          'public async Task<IActionResult> CreateNotices(WorkOrder wo, FakeUser assignee, bool urgent, string ownerName, bool smsStaged) {\n' +
          after_commit(server, 'public async Task<IActionResult> CreateWorkOrderFromAlert(', 'transaction.CommitAsync()') + '\n}\n}\n')

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--wasm-runner', help='Optional path to the Playpad WASM runner')
args = parser.parse_args()
with tempfile.TemporaryDirectory(prefix='workorder-controller-offline-') as directory:
    temp = Path(directory)
    (temp / 'ControllerExtract.cs').write_text(source, encoding='utf-8')
    for name in ('Program.cs', 'Shims.cs'):
        shutil.copyfile(HERE / name, temp / name)
    sources = [ROOT / 'src/Inventory.Api/Services/ItAssets/WorkOrderRequestIdentity.cs',
               ROOT / 'src/Inventory.Shared/Dtos/WorkOrderSmsDtos.cs']
    includes = '\n'.join(f'<Compile Include="{html.escape(str(p), quote=True)}" />' for p in sources)
    (temp / 'Offline.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
<ItemGroup>{includes}</ItemGroup></Project>''', encoding='utf-8')
    command = (['node', args.wasm_runner, str(temp / 'Offline.csproj')] if args.wasm_runner
               else ['dotnet', 'run', '--project', str(temp / 'Offline.csproj')])
    print('Extracted current controller code; using in-memory test fakes.', flush=True)
    subprocess.run(command, check=True)
