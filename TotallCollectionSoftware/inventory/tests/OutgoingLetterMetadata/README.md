# Outgoing-letter metadata placement

Synthetic tests of the real production QuestPDF/PDFsharp renderer, with real Forough artwork and MRTs. No API startup, DbContext, SMTP, or real letter is used.

From `inventory/`:

```sh
python -m unittest discover -s tests/OutgoingLetterMetadata -p 'test_*.py' -v
# Build the API first (normal .NET 8 build, or low-memory command below).
dotnet build tests/OutgoingLetterMetadata/OutgoingLetterMetadata.csproj -m:1 -p:BuildProjectReferences=false -p:ProduceReferenceAssembly=false -p:NuGetAudit=false
METADATA_TEST_OUTPUT=/path/to/artifacts dotnet tests/OutgoingLetterMetadata/bin/Debug/net8.0/OutgoingLetterMetadata.dll
# With PyMuPDF installed:
python tests/OutgoingLetterMetadata/check_rendered.py /path/to/artifacts
```

Results: 6 template tests, 58 renderer checks, 439 PDF text/bounding-box assertions across 19 PDFs. The dedicated existing `test_forough_family.py` also passes (11 tests). The broad legacy print suite has the same pre-existing failures on baseline and current source; see `../../OUTGOING-LETTER-METADATA-FIX-FA.md`.

Cases cover A4/A5, companies 1/2, attachment/no attachment, long number, no body margin, repeated headers on multiple pages, company-template mapping, edited MRT coordinates/cache stamp, and the embedded artwork fallback. Assertions inspect actual emitted glyph positions and complete text, not only source-string matches.

Memory-constrained API build used for this check (nullable/analyzer diagnostics off only for this command):

```sh
dotnet build src/Inventory.Api/Inventory.Api.csproj -m:1 \
  -p:UseSharedCompilation=false -p:RunAnalyzers=false -p:NuGetAudit=false \
  -p:DebugType=None -p:DebugSymbols=false -p:Optimize=true \
  -p:Nullable=annotations -p:Features=run-nullable-analysis=never \
  -p:ProduceOnlyReferenceAssembly=false -p:ProduceReferenceAssembly=false
```

Test code uses reflection to call the actual private render/composition methods; changing those signatures should update this harness. The independent Windows Stimulsoft engine is not exercised. The temporary edited MRT is removed after successful rendering because its legacy dictionary contains an unused encrypted connection definition; do not distribute that temporary file.
