const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('API registers remote services, hubs and RBAC module', () => {
    const program = read('src/Inventory.Api/Program.cs');
    assert.match(program, /IRemoteAuthService/);
    assert.match(program, /IRemoteSessionService/);
    assert.match(program, /RemoteSessionWorker/);
    assert.match(program, /MapHub<Inventory\.Api\.Hubs\.Remote\.RemoteHub>\("\/hubs\/remote"\)/);
    assert.match(program, /MapHub<Inventory\.Api\.Hubs\.Remote\.RemoteAgentHub>\("\/hubs\/remote-agent"\)/);
    assert.match(program, /MaximumReceiveMessageSize = 4 \* 1024 \* 1024/);
    const rbac = read('src/Inventory.Api/Data/RbacSeeder.cs');
    assert.match(rbac, /\["RemoteDesktop"\] = new\[\] \{ "View", "Control", "Manage" \}/);
});

test('remote schema is idempotent and initialized', () => {
    const schema = read('src/Inventory.Api/Data/RemoteSchemaV1.cs');
    assert.match(schema, /CREATE TABLE IF NOT EXISTS RemoteDevices/);
    assert.match(schema, /IF OBJECT_ID\(N'dbo\.RemoteDevices'/);
    const init = read('src/Inventory.Api/Data/DbInitializer.cs');
    assert.match(init, /RemoteSchemaV1\.EnsureAsync/);
});

test('remote sessions require consent and audit', () => {
    const svc = read('src/Inventory.Api/Services/Remote/RemoteSessionService.cs');
    assert.match(svc, /RequestConsent/);
    assert.match(svc, /ConsentAccepted/);
    assert.match(svc, /ConsentRejected/);
    assert.match(svc, /SessionEnded/);
    const agentHub = read('src/Inventory.Api/Hubs/Remote/RemoteAgentHub.cs');
    assert.match(agentHub, /X-Remote-Device-Key/);
    assert.match(agentHub, /AllowAnonymous/);
});

test('client pages and viewer script are wired', () => {
    const index = read('src/Inventory.Client/wwwroot/index.html');
    assert.match(index, /js\/remote-viewer\.js/);
    const settings = read('src/Inventory.Client/Pages/Settings/Index.razor');
    assert.match(settings, /\/settings\/remote/);
    assert.match(settings, /href="\/remote"/);
    const devices = read('src/Inventory.Client/Pages/Remote/RemoteDevices.razor');
    assert.match(devices, /@page "\/remote"/);
    const viewer = read('src/Inventory.Client/Pages/Remote/RemoteViewer.razor');
    assert.match(viewer, /JoinSession/);
    assert.match(viewer, /OnRemoteInput/);
});

test('remote agent project exists and uses device key', () => {
    const csproj = read('src/Inventory.RemoteAgent/Inventory.RemoteAgent.csproj');
    assert.match(csproj, /Microsoft\.AspNetCore\.SignalR\.Client/);
    const program = read('src/Inventory.RemoteAgent/Program.cs');
    assert.match(program, /X-Remote-Device-Key/);
    assert.match(program, /RequestConsent/);
    assert.match(program, /StartCapture/);
    const sln = read('Inventory.sln');
    assert.match(sln, /Inventory\.RemoteAgent/);
});
