"""Compiled Blazor + real appPush JS. PushManager/permission/provider are mocks, not Android delivery proof."""
import asyncio, importlib.util, json, threading, base64
from pathlib import Path
from urllib.parse import urlsplit
from playwright.async_api import async_playwright, expect
spec = importlib.util.spec_from_file_location('archive_ui', Path(__file__).resolve().parents[1] / 'doc-archive/ui_test.py')
fixture = importlib.util.module_from_spec(spec); spec.loader.exec_module(fixture)
ART = Path.home() / '.cache/push-ui'; ART.mkdir(parents=True, exist_ok=True)
KEY = base64.urlsafe_b64encode(bytes([4]) + bytes([7]) * 64).decode().rstrip('=')
MOCK = r'''
window.pushTest={permission:'default',sub:null,unsubscribes:0,subscribes:0,events:[],owner:0};
const p=window.pushTest;
Object.defineProperty(window,'Notification',{configurable:true,value:{get permission(){return p.permission;},requestPermission:async()=>{p.events.push('permission');p.permission='granted';return 'granted';}}});
Object.defineProperty(window,'PushManager',{configurable:true,value:function(){}});
const originalFetch=window.fetch;
window.fetch=(url,...args)=>{if(String(url).includes('push-vapid-key'))p.events.push('key');return originalFetch(url,...args);};
const reg={active:{postMessage:(data,ports)=>{p.owner=data.userId;localStorage.setItem('testOwner',String(p.owner));ports[0].postMessage({ok:true});}},pushManager:{
getSubscription:async()=>p.sub,
subscribe:async options=>{
 p.subscribes++;
 const sub={endpoint:'https://fcm.googleapis.com/wp/browser-'+p.subscribes,options,
 toJSON:()=>({endpoint:sub.endpoint,keys:{p256dh:'mock-public-key',auth:'mock-auth'}}),
 unsubscribe:async()=>{p.unsubscribes++;p.sub=null;return true;}};
 p.sub=sub;return sub;
}}};
Object.defineProperty(navigator,'serviceWorker',{configurable:true,value:{register:async()=>reg,getRegistration:async()=>reg,ready:Promise.resolve(reg),addEventListener:()=>{},controller:null}});
'''
async def main():
    async with async_playwright() as pw:
        browser=await pw.chromium.launch(headless=True,args=['--no-sandbox'])
        page=await browser.new_page(viewport=dict(width=1440,height=1000))
        errors=[]; saved=[]; removed=[]; tests=[]; server={'configured':True,'failSave':False,'registered':set()}
        page.on('pageerror',lambda e:errors.append(str(e)))
        async def api(route):
            path=urlsplit(route.request.url).path.lower();value=[]
            if '/hubs/' in path:return await route.fulfill(status=503,body='mock')
            if path.endswith('/push-vapid-key'):value=dict(configured=server['configured'],publicKey=KEY if server['configured'] else '')
            elif path.endswith('/push-subscribe'):
                if server['failSave']:return await route.fulfill(status=503,json=dict(message='ثبت دستگاه در سرور ناموفق بود.'))
                payload=route.request.post_data_json;saved.append(payload);server['registered'].add(payload['endpoint']);value=dict(ok=True)
                assert route.request.headers.get('authorization')=='Bearer push-test-token'
            elif path.endswith('/push-status'):value=dict(configured=True,registered=route.request.post_data_json['endpoint'] in server['registered'],last=None)
            elif path.endswith('/push-unsubscribe'):
                endpoint=route.request.post_data_json['endpoint'];removed.append(endpoint);server['registered'].discard(endpoint);value=dict(ok=True)
            elif path.endswith('/test-push'):
                tests.append(route.request.post_data_json);return await route.fulfill(status=202,json=dict(message='اعلان در صف ارسال قرار گرفت؛ دریافت آن را در نوار اعلان گوشی بررسی کنید.'))
            elif path.endswith('/doc-archive/documents/1'):value=dict(fixture.docs[0],versions=[])
            elif path.endswith('/doc-archive/lookups'):value=dict(users=[],roles=[],documents=[])
            elif path.endswith('/doc-archive/folders'):value=fixture.folders
            elif path.endswith('/doc-archive/tags'):value=fixture.tags
            elif path.endswith('/unread-count'):value=dict(count=0)
            await route.fulfill(content_type='application/json',body=json.dumps(value))
        await page.route('**/api/**',api);await page.route('**/hubs/**',api)
        auth=dict(UserId=1,Token='push-test-token',Username='test',Role='Admin',DisplayName='آزمایش',Permissions=['DocArchive.Read','DocArchive.Manage'])
        await page.add_init_script(MOCK+'localStorage.setItem("authSession",'+json.dumps(json.dumps(auth))+');localStorage.setItem("apiBaseUrl",location.origin);')
        await page.goto(fixture.base+'/doc-archive/documents/1')
        try: await page.get_by_title('اعلان‌ها',exact=True).wait_for(timeout=60000)
        except Exception:
            print('ERRORS',errors,'BODY',await page.locator('body').inner_text());raise
        await page.get_by_title('اعلان‌ها',exact=True).click()
        panel=page.locator('.ntf-panel');message=panel.locator('.ntf-push-msg')
        enable=panel.get_by_title('تنظیم اعلان این دستگاه',exact=True)
        await expect(enable).to_be_enabled()
        await page.evaluate('pushTest.events=[]')
        await enable.click()
        await expect(panel.get_by_role('button',name='آزمایش',exact=True)).to_be_visible()
        assert await page.evaluate('pushTest.events')==['permission','key']
        assert saved and await page.evaluate('pushTest.owner')==1
        await panel.get_by_role('button',name='آزمایش',exact=True).click()
        await expect(message).to_contain_text('در صف ارسال')
        assert tests[-1]['endpoint']==saved[-1]['endpoint']
        await page.screenshot(path=str(ART/'desktop.png'),full_page=True)
        # Local subscription survives, but server forgot it: state check replaces and saves it.
        server['registered'].clear();before=await page.evaluate('pushTest.subscribes')
        await panel.get_by_role('button',name='بررسی وضعیت',exact=True).click()
        await expect(message).to_contain_text('این دستگاه ثبت شد')
        assert await page.evaluate('pushTest.subscribes')==before+1
        await panel.get_by_title('تنظیم اعلان این دستگاه',exact=True).click()
        await expect(message).to_contain_text('خاموش شد');assert removed
        assert await page.evaluate('pushTest.owner')==0
        # A provider subscription is not falsely reported enabled when DB registration fails.
        server['failSave']=True
        await enable.click();await expect(message).to_contain_text('ثبت دستگاه در سرور ناموفق')
        assert await page.evaluate('pushTest.sub') is None
        await expect(panel.get_by_role('button',name='آزمایش',exact=True)).to_have_count(0)
        server['failSave']=False;server['configured']=False
        await panel.get_by_role('button',name='بررسی وضعیت',exact=True).click()
        await expect(message).to_contain_text('کلیدهای اعلان روی سرور تنظیم نشده')
        server['configured']=True
        await page.evaluate("pushTest.permission='denied'")
        await panel.get_by_role('button',name='بررسی وضعیت',exact=True).click()
        await expect(message).to_contain_text('اجازه اعلان مسدود است')
        # Mobile layout, enable and logout revoke registration before clearing auth.
        await page.set_viewport_size(dict(width=390,height=844))
        await page.evaluate("pushTest.permission='granted'")
        await enable.click();await expect(panel.get_by_role('button',name='آزمایش',exact=True)).to_be_visible()
        await page.screenshot(path=str(ART/'mobile.png'),full_page=True)
        assert await page.evaluate('document.documentElement.scrollWidth<=innerWidth')
        endpoint=saved[-1]['endpoint']
        await page.get_by_title('اعلان‌ها',exact=True).click()
        await page.get_by_title('خروج',exact=True).click()
        await page.wait_for_url(fixture.base+'/',timeout=30000)
        assert endpoint in removed
        assert await page.evaluate("localStorage.getItem('push-opt-in-user')") is None
        assert await page.evaluate("localStorage.getItem('testOwner')")=='0'
        assert not errors,errors
        print('PASS: compiled desktop/mobile push UI, permission-before-network, save verification/rollback, missing VAPID, denied permission, resubscription, queued test, logout cleanup; no pageerrors')
        await browser.close()
if __name__=='__main__':
    threading.Thread(target=fixture.server.serve_forever,daemon=True).start()
    try:asyncio.run(main())
    finally:fixture.server.shutdown();fixture.server.server_close()
