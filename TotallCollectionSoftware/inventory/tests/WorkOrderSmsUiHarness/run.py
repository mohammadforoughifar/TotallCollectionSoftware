#!/usr/bin/env python3
"""Exercise extracted production work-order status methods and API with fakes.

This fixture never renders Razor, hosts ASP.NET, uses real EF or contacts a provider.
It runs the current methods extracted from the real files; HTTP, EF and the timer are
controlled in memory. With .NET 8: python tests/WorkOrderSmsUiHarness/run.py
An optional WASM runner can instead accept a list of C# source-file arguments:
  python tests/WorkOrderSmsUiHarness/run.py --wasm-runner /path/to/run-wasm.mjs
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
ui = (ROOT / 'src/Inventory.Client/Pages/ItAssets/WorkOrders.razor').read_text(encoding='utf-8')
api = (ROOT / 'src/Inventory.Api/Controllers/ItAssets/WorkOrdersController.cs').read_text(encoding='utf-8')


def block(source: str, signature: str) -> str:
    start = source.index('    ' + signature)
    return source[start:source.index('\n    }', start) + len('\n    }')]


def expression(source: str, signature: str) -> str:
    start = source.index('    ' + signature)
    return source[start:source.index(';', start) + 1]


watch = block(ui, 'private void WatchPendingSmsEvents(')
assert watch.count('_ = smsStatusPoller.StartAsync(') == 1
# Preserve the same call and callback but retain the returned task so the test can
# deterministically await completion of a fire-and-forget watch.
watch = watch.replace('_ = smsStatusPoller.StartAsync(', 'lastWatch = smsStatusPoller.StartAsync(')
methods = [
    block(ui, 'private void CancelSmsDetailReads('),
    expression(ui, 'private bool IsCurrentSmsDetail('),
    block(ui, 'private Task BeginSmsStatusSession('),
    block(ui, 'private async Task RefreshSmsEvents('),
    block(ui, 'private async Task LoadInitialSmsEvents('),
    watch,
    block(ui, 'private async Task LoadSmsEvents('),
    block(ui, 'private void CloseDetail('),
    block(ui, 'public void Dispose('),
]
endpoint = block(api, 'public async Task<IActionResult> SmsEvents(')
source = '''#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Client.Pages;
using Inventory.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

public sealed partial class StatusHarness {
''' + '\n\n'.join(methods) + '''
}
public sealed partial class EndpointHarness {
''' + endpoint + '''
}
'''

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--wasm-runner', help='Optional WASM C# runner accepting source-file arguments')
args = parser.parse_args()
with tempfile.TemporaryDirectory(prefix='workorder-sms-ui-') as directory:
    temp = Path(directory)
    (temp / 'ProductionExtract.cs').write_text(source, encoding='utf-8')
    for name in ('Program.cs', 'Shims.cs'):
        shutil.copyfile(HERE / name, temp / name)
    sources = [
        temp / 'Program.cs', temp / 'Shims.cs', temp / 'ProductionExtract.cs',
        ROOT / 'src/Inventory.Client/Pages/ItAssets/WorkOrderSmsAppearance.cs',
        ROOT / 'src/Inventory.Client/Pages/ItAssets/WorkOrderSmsStatusPoller.cs',
        ROOT / 'src/Inventory.Shared/Dtos/WorkOrderSmsDtos.cs',
        ROOT / 'src/Inventory.Shared/KavenegarCodeText.cs',
        ROOT / 'src/Inventory.Api/Entities/ItAssets/WorkOrderSmsNotification.cs',
    ]
    if args.wasm_runner:
        command = ['node', args.wasm_runner, *(str(p) for p in sources)]
    else:
        includes = '\n'.join(f'<Compile Include="{html.escape(str(p), quote=True)}" />' for p in sources)
        (temp / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
<Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
<ItemGroup>{includes}</ItemGroup></Project>''', encoding='utf-8')
        command = ['dotnet', 'run', '--project', str(temp / 'Harness.csproj')]
    print('Extracted current UI and API methods; HTTP, database and timer are fakes.', flush=True)
    subprocess.run(command, check=True)
