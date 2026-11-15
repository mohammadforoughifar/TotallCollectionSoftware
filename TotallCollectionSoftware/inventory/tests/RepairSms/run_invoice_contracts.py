"""Run production InvoiceAsync with fake inventory, DB, transactions and SMS queue.

This checks control flow, not EF translation, real stock/accounting or concurrency.
Use --emit /tmp/invoice-contract.cs to run with another C# compiler.
"""
from pathlib import Path
import argparse
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--emit', type=Path)
    args = parser.parse_args()
    template = (HERE / 'InvoiceHarness.cs.txt').read_text(encoding='utf-8')
    api = (ROOT / 'src/Inventory.Api/Services/Repairs/RepairService.cs').read_text(encoding='utf-8')
    start = '    public async Task<RepairOrderDto> InvoiceAsync('
    end = '    private async Task<int> WarrantyInvoiceIdAsync('
    source = start + api.split(start, 1)[1].split(end, 1)[0]
    marker = '    // <PRODUCTION_INVOICE_METHOD>'
    if template.count(marker) != 1:
        raise RuntimeError('Missing or duplicated production invoice marker')
    source = template.replace(marker, source)
    if args.emit:
        args.emit.write_text(source, encoding='utf-8')
        print(f'Generated {args.emit}')
        return 0
    if not shutil.which('dotnet'):
        print('A .NET 8 SDK is required (or use --emit with another C# runner).', file=sys.stderr)
        return 1
    with tempfile.TemporaryDirectory(prefix='repair-invoice-contract-') as directory:
        root = Path(directory)
        (root / 'Program.cs').write_text(source, encoding='utf-8')
        (root / 'Harness.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
</Project>''', encoding='utf-8')
        return subprocess.run(['dotnet', 'run', '--project', str(root / 'Harness.csproj')], check=False).returncode


if __name__ == '__main__':
    sys.exit(main())
