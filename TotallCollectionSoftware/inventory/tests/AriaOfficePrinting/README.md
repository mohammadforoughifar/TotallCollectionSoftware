# Aria/1005 — internal letters and meeting minutes

This suite exercises the production assembly without starting the API or accessing a database. It covers the **internal-letter and meeting-minute** paths, separately from `tests/AriaLetterhead` (outgoing letters).

## Coverage

- A4/A5: native PNG artwork from the corresponding approved outgoing MRT, exact metadata boxes and original bindings.
- Internal letters: configured DPI 0/150/200/300/600, warm caches, long numbers, body overflow, enabled/disabled hamesh, 35 hamesh rows, absent metadata, and a missing individual date field.
- Minutes: attached/unattached, maximum integer ID, meeting date rather than registration date, 24 items, 13 attendees, absentees, empty lists, synthetic image/blank/corrupt signatures, and the existing no-metadata retry.
- A temporary A5 MRT moves **only the date** two points left and 1.5 points down. Every other box stays fixed. Template-version cache invalidation is tested. Temporary MRTs, including inherited dictionaries, are always deleted.
- Other-company smoke PDFs use the actual Homepa/1003 and Forough/1 production paths. Optional before/after comparison checks every 300-DPI page pixel and extracted text.
- The static suite protects every non-artwork/non-metadata XML node (including original data bindings), runtime/design-folder equality, updater idempotence, and unchanged builder output for companies 1/3/1003.

The synthetic signature is a blue crossed rectangle, **not a person's autograph**. No records, referrals, signature state, real messages or database tables are changed.

## Run

From `inventory/`, using .NET 8 and Python with `Pillow`, `PyMuPDF`, and `fontTools`:

```sh
python -m unittest discover -s tests/AriaOfficePrinting -p 'test_*.py' -v
# Build the API first, then the small probe against that exact assembly.
dotnet build src/Inventory.Api/Inventory.Api.csproj
dotnet build tests/AriaOfficePrinting/AriaOfficePrinting.csproj -p:BuildProjectReferences=false
ARIA_OFFICE_OUTPUT=/absolute/path/to/artifacts \
  dotnet tests/AriaOfficePrinting/bin/Debug/net8.0/AriaOfficePrinting.dll
python tests/AriaOfficePrinting/check_rendered.py /absolute/path/to/artifacts
```

`ARIA_OFFICE_MODE` can be `all` (default), `aria`, or `regression`. `ARIA_OFFICE_API_ROOT` optionally points at a matching source/asset checkout. The output contains 31 Aria documents, four artwork-only references, and (in `all` mode) eight other-company PDFs.

To compare other companies with the pre-change renderer, run this same probe in `regression` mode against the pre-change API assembly, with the unchanged company 1/1003 assets. Then supply that output directory as the checker's second argument:

```sh
python tests/AriaOfficePrinting/check_rendered.py /current/artifacts /prechange/regression-artifacts
```

The evidence for this change used the API assembly saved **before** the internal/minutes extension; that assembly already included the completed outgoing Aria fix. Production methods, not copies of the renderers, produced both sets.

## PDF validation details

The checker verifies full metadata values, digit order, single baselines, glyph bounds, full white value masks, pixel-identical printed captions/colons/logo/footer, and the native image's RGB pixel hash in every PDF page. It checks body/footer clearance, complete fixture markers, all hamesh/participant names, signed/unsigned image behavior, and signature positions after the final item. Minutes retain their existing header-only continuation artwork; internal letters retain their full-page repeated background.

Some Vazirmatn contextual elongated glyphs have no `ToUnicode` entry; MuPDF returns a glyph ID instead. For body-content assertions only, the checker resolves those IDs via the bundled font's `uniXXXX` glyph names and reconstructs RTL lines from physical glyph positions, preserving LTR digit runs. This is **test extraction logic**, not a production font/text modification. Metadata remains independently checked glyph by glyph in its physical box.

## Verification recorded for this change

- 11 static tests; 254 production loader/render checks.
- 1,944 PDF assertions across 31 Aria documents, including eight pixel-identical other-company comparisons.
- Outgoing regressions: Aria 102 render + 542 PDF checks (15 letters); Forough 58 render + 439 PDF checks (19 PDFs).
- Constrained API build: zero errors, three existing SNMP obsolescence warnings, 86.37 seconds.

The memory-constrained sandbox build used `-m:1`, `UseSharedCompilation=false`, `RunAnalyzers=false`, `NuGetAudit=false`, `DebugType=None`, `DebugSymbols=false`, `Optimize=true`, `Nullable=annotations`, `Features=run-nullable-analysis=never`, `ProduceOnlyReferenceAssembly=false`, and `ProduceReferenceAssembly=false` as **command-line overrides only**. This is not a full default-CI, publish or API-startup test.

Existing broad suites are still red on both baseline and current checkouts with identical failing IDs: outgoing/template suite 208 tests / 23 failures / 172 errors; old internal suite 39 / 3 / 2. No unrelated legacy failures were hidden or repaired.

No live database/SMTP, Windows Stimulsoft desktop engine, physical-printer, deployment, commit or push validation is implied.
