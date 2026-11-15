const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('API registers backup services and hosted worker', () => {
    const program = read('src/Inventory.Api/Program.cs');
    assert.match(program, /AddHttpClient\("backup"/);
    assert.match(program, /IBackupSettingsService/);
    assert.match(program, /ISqlServerBackupService/);
    assert.match(program, /AddHostedService<Inventory\.Api\.Services\.Backup\.BackupWorker>/);
});

test('schema is idempotent and initialized', () => {
    const schema = read('src/Inventory.Api/Data/BackupSchemaV1.cs');
    assert.match(schema, /IF OBJECT_ID\(N'dbo\.BackupSettings'/);
    assert.match(schema, /CREATE TABLE IF NOT EXISTS BackupSettings/);
    const init = read('src/Inventory.Api/Data/DbInitializer.cs');
    assert.match(init, /BackupSchemaV1\.EnsureAsync/);
});

test('SQL backup uses COPY_ONLY, CHECKSUM and VERIFYONLY', () => {
    const svc = read('src/Inventory.Api/Services/Backup/SqlServerBackupService.cs');
    assert.match(svc, /COPY_ONLY, CHECKSUM/);
    assert.match(svc, /RESTORE VERIFYONLY/);
    assert.match(svc, /SERVERPROPERTY\('Edition'\)/);
    assert.match(svc, /ZipFile\.Open/);
});

test('central upload is idempotent and verifies SHA-256', () => {
    const storage = read('src/Inventory.Api/Services/Backup/CentralBackupStorageService.cs');
    assert.match(storage, /existing != null/);
    assert.match(storage, /duplicate = true/);
    assert.match(storage, /Sha256FileAsync/);
    assert.match(storage, /App_Data", "CentralBackups/);
});

test('client pages and heartbeat script are wired', () => {
    const index = read('src/Inventory.Client/wwwroot/index.html');
    assert.match(index, /js\/backup-session\.js/);
    const layout = read('src/Inventory.Client/Layout/MainLayout.razor');
    assert.match(layout, /IBackupSessionTracker/);
    assert.match(layout, /BackupTracker\.StartAsync/);
    assert.match(layout, /BackupTracker\.StopAsync/);
    const settings = read('src/Inventory.Client/Pages/Settings/Index.razor');
    assert.match(settings, /\/settings\/backup/);
    assert.match(settings, /\/settings\/central-backup/);
});
