# Company logo support

Company add/edit now accepts PNG/JPEG (up to 2 MiB), previews it and supports
removal on save. Logo data belongs to SystemCompany, independent of the existing
PDF letterhead. Legacy rows have NULL logos; startup repair covers SQLite/SQL
Server and a migration covers SQL Server deployments. Images are stored as a
self-contained data URI in the database, so there is no arbitrary file path/URL
or uploaded executable file, and database backups include the logo.

The server checks declared image kind, byte size, decoded format and dimensions
(up to 2048 by 2048), decodes just the first frame and re-encodes as PNG, stripping
EXIF/ICC/XMP metadata. Decoded output size is also bounded. Only company add/edit
writes logo data; company-profile GET exposes the active company's logo read-only.
Office PDF letterhead upload and print behavior are unchanged.

Shop sales/purchase print builders and delivery-note printing consume this company
logo instead of a demo logo or letter mark when available; a letter fallback remains
for older companies without an image. Shop print settings displays it read-only.
The print entry refreshes company profile so a previously cached logo is not used.

Validation here:
- Python tests exercise source contracts and actual SQLite legacy-column/storage.
- Executable .NET image tests cover PNG/JPEG, clear, malformed formats and bounds,
  but are **not run** because the .NET SDK is unavailable.
- Full authenticated company create/edit/reload, C#/Razor compilation and SQL Server
  migration still need execution in a .NET-enabled environment.
