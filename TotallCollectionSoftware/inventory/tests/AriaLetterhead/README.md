# Aria Hamyar Generator outgoing letterhead

Company/design **1005 only**, A4/A5. The supplied CMYK JPEGs are converted through their embedded ICC profile into full-resolution sRGB PNGs inside the MRTs; no resampling or second lossy encoding. The three metadata rectangles match the new dotted row. Body/signature flow and other companies are preserved.

## Tests

From `inventory/`, with .NET 8, Pillow and PyMuPDF installed:

```sh
python -m unittest discover -s tests/AriaLetterhead -p 'test_*.py' -v
# Build API first; see memory-constrained alternative below.
dotnet build src/Inventory.Api/Inventory.Api.csproj

dotnet build tests/AriaLetterhead/AriaLetterhead.csproj -m:1 \
  -p:BuildProjectReferences=false -p:ProduceReferenceAssembly=false -p:NuGetAudit=false
ARIA_TEST_OUTPUT=/path/to/artifacts dotnet tests/AriaLetterhead/bin/Debug/net8.0/AriaLetterhead.dll
python tests/AriaLetterhead/check_rendered.py /path/to/artifacts
```

Expected: **9 template tests**, **102 render/resolver checks**, and **542 actual PDF glyph/geometry/image assertions** across **15 synthetic letters** plus 2 artwork-only references.

The probe invokes the real private `ExtractTemplateLetterhead`, `BuildContentPdf` and `OverlayOnLetterhead` methods. It does not build a DbContext, start the API, connect to mail/SQL, use actual signatures or render through Windows Stimulsoft. Source preference checks cover default settings, shipped appsettings and a mapped company; they are not a live database integration test.

Cases: both sizes; attachment/no attachment; optional margin toggle; long number; three-page body; copies and synthetic signer names/roles; company 501 mapped to 1005; customized MRT coordinates/cache stamp; calibrated fallback when fields are absent. Temporary customized MRT copies (including the inherited unused connection dictionary) are deleted in `finally`.

PDF validation works on glyph coordinates because extractors can merge adjacent horizontal fields into a single text span. It checks all digits/order and a single baseline per field, complete white plaques, unchanged caption/colon pixels, logo/footer preservation, safe body bounds and the exact SHA-256 of decoded native-resolution artwork pixels. Reference and final pages are rasterized once at 300 DPI before cropping so clipped-image raster cache behavior cannot affect comparisons.

### Regressions

Run `tests/OutgoingLetterMetadata` and its checker: 58 Forough render checks and 439 PDF assertions across 19 PDFs must still pass. `tests/OutgoingLetterhead/test_forough_family.py` plus focused Aria/Homepa contracts are also covered. The full legacy suite has pre-existing failures; the independently archived `8da3192` baseline and this change have identical normalized failing IDs (208 tests, 23 failures, 172 errors), not a fully green legacy suite.

## Recreate the artwork update

Use the exact original uploads; source SHA-256 values and native dimensions are in `artwork-manifest.json`:

```sh
python tools/update_aria_letterhead.py \
  --a4 '/path/to/Sarbarg    A4.jpg' \
  --a5 '/path/to/Sarbarg    A5.jpg'
```

The updater only changes company 1005's outgoing `Watermark.ImageBytes` and `Text1/Text2/Text3`. It asserts the remaining canonical XML is unchanged. Original JPEGs are not needed for normal application operation or test validation because each MRT contains its image.

## Low-memory API build

These overrides were used only on the build command line, not in project files; they do not replace normal CI/publish checks:

```sh
DOTNET_PROCESSOR_COUNT=1 DOTNET_GCHeapHardLimit=0x38000000 DOTNET_GCConserveMemory=9 \
  dotnet build src/Inventory.Api/Inventory.Api.csproj -m:1 \
    -p:UseSharedCompilation=false -p:RunAnalyzers=false -p:NuGetAudit=false \
    -p:DebugType=None -p:DebugSymbols=false -p:Optimize=true \
    -p:Nullable=annotations -p:Features=run-nullable-analysis=never \
    -p:ProduceOnlyReferenceAssembly=false -p:ProduceReferenceAssembly=false
```

Publish the API, both MRTs and the Aria template-source preference together. Add 1005 to any environment-specific `Reporting:OutgoingLetters:TemplateLetterheadCompanyIds` override while retaining other entries. Keep `DesignDrivenCompanyIds` unchanged. See `../../ARIA-LETTERHEAD-UPDATE-FA.md`.
