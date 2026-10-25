# User signature RGBA regression test

Run from the repository root:

```bash
dotnet run --project inventory/tests/UserSignatureTransparency/UserSignatureTransparency.csproj
```

The probe uses an in-memory SQLite database and the production `AuthController`, `FileStore`, and outgoing-letter PDF builder. It uploads an RGBA PNG containing both a transparent corner and white paper around the ink, checks the stored PNG IHDR is RGBA (color type 6), verifies alpha-zero and ink pixels after reloading, then exercises the PDF renderer and PDFsharp letterhead overlay.
