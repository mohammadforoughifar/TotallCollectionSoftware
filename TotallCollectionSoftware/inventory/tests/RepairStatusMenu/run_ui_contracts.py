"""Run real status-handler methods with fake services; not a Razor/EF test.

With a .NET 8 SDK: python3 tests/RepairStatusMenu/run_ui_contracts.py
To use another C# runner, emit the generated single-file harness:
    python3 tests/RepairStatusMenu/run_ui_contracts.py --emit /tmp/repair-status-contract.cs
"""
from pathlib import Path
import argparse
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent


def part(text, start, end):
    return start + text.split(start, 1)[1].split(end, 1)[0]


def generated_source():
    def read(path):
        return (ROOT / path).read_text(encoding='utf-8')

    def namespace_source(path, namespace):
        # Turn a file-scoped namespace into a block so multiple production files
        # can be compiled together in a single portable source file.
        body = read(path).split(f'namespace {namespace};', 1)[1]
        return f'namespace {namespace} {{\n{body}\n}}\n'

    page = read('src/Inventory.Client/Pages/Repairs/Repairs.razor')
    template = (HERE / 'UiHarness.cs.txt').read_text(encoding='utf-8')
    service = read('src/Inventory.Api/Services/Repairs/RepairService.cs')
    policy = read('src/Inventory.Api/Services/Repairs/RepairSmsPolicy.cs')
    service_harness = (HERE / 'ServiceHarness.cs.txt').read_text(encoding='utf-8')
    service_harness = service_harness.replace('// <PRODUCTION_SERVICE_STATUS>', part(service,
        '    public async Task<RepairOrderDto> SetStatusAsync(', '    public async Task<RepairOrderDto> InvoiceAsync('))
    service_harness = service_harness.replace('// <PRODUCTION_EVENT_POLICY>', part(policy,
        '    public static RepairSmsEventType? EventFor(', '    public static RepairSmsStatus InitialStatus('))
    appearance = read('src/Inventory.Client/Pages/Repairs/RepairStatusAppearance.cs')
    warranty_enum = next(line for line in read('src/Inventory.Shared/Dtos/ShopWarrantyDtos.cs').splitlines()
                         if line.startswith('public enum WarrantyDecision '))
    catalog = read('src/Inventory.Shared/Dtos/CatalogDtos.cs')
    treasury = read('src/Inventory.Shared/Dtos/TreasuryDtos.cs')
    real_types = (namespace_source('src/Inventory.Shared/Enums.cs', 'Inventory.Shared') +
                  namespace_source('src/Inventory.Shared/ShopSections.cs', 'Inventory.Shared') +
                  namespace_source('src/Inventory.Shared/Dtos/RepairDtos.cs', 'Inventory.Shared.Dtos') +
                  'namespace Inventory.Shared.Dtos {\n' + warranty_enum + '\n' +
                  part(catalog, 'public class Warehouse\n', '/// <summary>طرف حساب') +
                  part(treasury, 'public class TrsAccount\n', '// ============================ ۲) سند خزانه') + '\n}\n' +
                  # Only the small interface is stubbed; the access policy itself is production code.
                  'namespace Inventory.Client.Services { public interface IAuthState { bool IsAdmin { get; } '
                  'bool Has(string p); bool HasDirect(string p); bool HasModule(string m); bool HasModuleDirect(string m); } }\n' +
                  namespace_source('src/Inventory.Client/Services/ModuleAccess.cs', 'Inventory.Client.Services') +
                  namespace_source('src/Inventory.Client/Services/ShopAccess.cs', 'Inventory.Client.Services'))
    pieces = {
        '// <PRODUCTION_SHARED_AND_ACCESS>': real_types,
        '// <PRODUCTION_APPEARANCE>': 'internal static class RepairStatusAppearance' + appearance.split('internal static class RepairStatusAppearance', 1)[1],
        '// <PRODUCTION_FIELDS>': part(page, '    private static readonly RepairStatus[] StatusChoices =', '    protected override async Task OnInitializedAsync()'),
        '// <PRODUCTION_SMS_METHODS>': part(page, '    private static string SmsEventName(', '    private static string SmsSummary('),
        '// <PRODUCTION_PERMISSION_AND_IDS>': part(page, '    private bool CanChangeStatus(RepairOrderDto r)', '    private void ToggleStatusMenu('),
        '// <PRODUCTION_MENU_METHODS>': part(page, '    private void ToggleStatusMenu(RepairOrderDto r, string view)', '    // ---------------- فرم ----------------'),
        '// <PRODUCTION_INVOICE_METHODS>': part(page, '    private static bool IsFreeWarranty(RepairOrderDto repair)', '    private async Task PrintDeliveryReceipt('),
        '// <SERVICE_HARNESS>': service_harness,
    }
    for marker, code in pieces.items():
        if template.count(marker) != 1:
            raise RuntimeError(f'Missing or duplicated harness marker: {marker}')
        template = template.replace(marker, code)
    return template


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--emit', type=Path, help='write the generated source instead of running dotnet')
    args = parser.parse_args()
    source = generated_source()
    if args.emit:
        args.emit.write_text(source, encoding='utf-8')
        print(f'Generated {args.emit}')
        return 0
    if not shutil.which('dotnet'):
        print('A .NET 8 SDK is required for this harness (or use --emit with another C# runner).', file=sys.stderr)
        return 1
    with tempfile.TemporaryDirectory(prefix='repair-status-contract-') as directory:
        root = Path(directory)
        (root / 'Program.cs').write_text(source, encoding='utf-8')
        (root / 'Harness.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
</Project>''', encoding='utf-8')
        return subprocess.run(['dotnet', 'run', '--project', str(root / 'Harness.csproj')], check=False).returncode


if __name__ == '__main__':
    sys.exit(main())
