"""UI regression check against real Blazor WASM with mock APIs; no production server/data required."""
import asyncio,json,os,threading
from pathlib import Path
from http.server import SimpleHTTPRequestHandler,ThreadingHTTPServer
from urllib.parse import unquote
from urllib.parse import urlsplit
from playwright.async_api import async_playwright
CLIENT=Path(__file__).resolve().parents[2]/'inventory/src/Inventory.Client'
ARTIFACTS=Path(os.environ.get('WO_UI_ARTIFACTS',str(Path.home()/'.cache/work-order-ui')))
ARTIFACTS.mkdir(parents=True,exist_ok=True)
class StaticClient(SimpleHTTPRequestHandler):
    extensions_map={**SimpleHTTPRequestHandler.extensions_map,'.wasm':'application/wasm','.js':'text/javascript'}
    def translate_path(self,path):
        rel=unquote(urlsplit(path).path).lstrip('/')
        if rel == 'Inventory.Client.styles.css':
            return str(CLIENT/'obj/Debug/net8.0/scopedcss/bundle/Inventory.Client.styles.css')
        if '..' not in Path(rel).parts:
            for root in [CLIENT/'bin/Debug/net8.0/wwwroot',CLIENT/'wwwroot']:
                if (root/rel).is_file(): return str(root/rel)
        return str(CLIENT/'wwwroot/index.html')
    def log_message(self,*args): pass
server=ThreadingHTTPServer(('127.0.0.1',0),StaticClient)
base=f'http://127.0.0.1:{server.server_port}'
permissions=['WorkOrders.View','WorkOrders.Create','WorkOrders.AssignOthers','WorkOrders.Delete','Chat.View','Chat.Send']
users=[dict(id=1,username='محمد رضایی'),dict(id=2,username='علی احمدی'),dict(id=3,username='سارا کریمی')]
assignees=[dict(id=2,userId=2,name='علی احمدی'),dict(id=3,userId=3,name='سارا کریمی')]
def order(i,title,**kw):
    return dict(id=i,number=f'WO/1405/{i}',title=title,description='<p>بازدید تجهیزات و ثبت نتیجه بررسی در پایان شیفت.</p>',ownerUserId=1,ownerName='محمد رضایی',dueAt='2026-10-14T14:00:00',createdAt='2026-09-14T09:30:00',status='Open',priority=1,recurrence=0,tags=['تعمیرات'],assignees=assignees,children=[],**kw)
a=order(101,'بازدید و سرویس تجهیزات شعبه مرکزی')
b=order(102,'بررسی تابلو برق',parentOrderId=101,parentNumber=a['number'],parentTitle=a['title'])
a['children']=[dict(id=102,number=b['number'],title=b['title'],status='Open',ownerName='محمد رضایی')]
orders={101:a,102:b}
async def main():
    async with async_playwright() as pw:
        browser=await pw.chromium.launch(headless=True,args=['--no-sandbox'])
        page=await browser.new_page(viewport=dict(width=1440,height=1000),device_scale_factor=1)
        captured=[]
        errors=[]
        page.on('pageerror',lambda err:errors.append(str(err)))
        await page.add_init_script('localStorage.setItem("authSession",'+json.dumps(json.dumps(dict(UserId=1,Token='ui-test-token',Username='test',Role='Admin',DisplayName='محمد رضایی',Permissions=permissions)))+'); localStorage.setItem("apiBaseUrl",location.origin);')
        async def api(route):
            path=urlsplit(route.request.url).path.lower()
            value={}
            if path == '/api/workorders' and route.request.method == 'POST':
                captured.append(route.request.post_data_json)
                return await route.fulfill(content_type='application/json',body=json.dumps(dict(id=201,number='WO/1405/201',occurrenceCount=6)))
            if '/hubs/' in path: return await route.fulfill(status=503,body='test transport disabled')
            if path.endswith('/workorders/my-access'): value=dict(userId=1,canView=True,canCreate=True,canAssignOthers=True,canDelete=True)
            elif path.endswith('/workorders/mine'): value=list(orders.values())
            elif path.endswith(('/workorders/assigned','/workorders/archive','/workorders/templates','/notifications')): value=[]
            elif path.endswith('/workorders/targets'): value=users
            elif '/api/workorders/' in path:
                tail=path.split('/api/workorders/')[1]
                if tail.isdigit(): value=orders.get(int(tail),{})
                elif tail.endswith('/checklist'): value=[dict(id=1,orderId=int(tail.split('/')[0]),text='بررسی اولیه',sortOrder=0,isDone=False)]
                elif tail.endswith('/comments'): value=[dict(id=1,authorUserId=2,authorName='علی احمدی',text='بازدید در شیفت بعد انجام می‌شود.',createdAt='2026-09-14T10:00:00',isDeleted=False)]
                elif tail.endswith('/logs'): value=[dict(id=1,actorName='محمد رضایی',action='Created',text='ثبت دستور کار',createdAt='2026-09-14T09:30:00')]
                else: value=[]
            elif '/api/attachments/' in path: value=[]
            elif '/photo' in path: return await route.fulfill(status=404,body='')
            elif '/systeminfo' in path: value=[]
            elif '/chat/conversations' in path: value=[]
            return await route.fulfill(content_type='application/json',body=json.dumps(value))
        await page.route('**/api/**',api)
        await page.route('**/hubs/**',api)
        await page.goto(base+'/work-orders')
        try:
            await page.locator('.wo-page-action').wait_for(timeout=30000)
        except Exception:
            print('ERRORS', errors, 'BODY', await page.locator('body').inner_text())
            await page.screenshot(path=str(ARTIFACTS/'wo-ui-fail.png'),full_page=True)
            raise
        await page.locator('.grid-desktop .wo-related-link').first.wait_for()
        await page.screenshot(path=str(ARTIFACTS/'wo-list-desktop.png'),full_page=True)
        await page.locator('.wo-page-action').click()
        await page.locator('.wo-form-modal').wait_for()
        await page.screenshot(path=str(ARTIFACTS/'wo-form-desktop.png'),full_page=True)
        # Real Blazor handlers: recipient popup, recurrence, optional fields and rich text.
        await page.locator('.wo-form-modal input[placeholder="عنوان دستور کار"]').fill('آزمون ثبت فرم')
        await page.locator('.wo-form-modal .ms-combo-control').click()
        await page.locator('.ms-combo-item').filter(has_text='علی احمدی').click()
        await page.locator('.pop-overlay').click(position=dict(x=5,y=5))
        await page.locator('#wo-recurrence').select_option('1')
        await page.locator('.wo-form-modal .rte-editor').fill('متن ثبت‌شده توسط کاربر')
        await page.locator('.wo-form-extras summary').filter(has_text='چک‌لیست').click()
        await page.get_by_placeholder('عنوان مرحله').fill('مرحله تست')
        await page.get_by_placeholder('عنوان مرحله').press('Enter')
        await page.locator('.wo-form-extras summary').filter(has_text='برچسب‌ها').click()
        await page.get_by_placeholder('برچسب جدید').fill('بررسی')
        await page.get_by_placeholder('برچسب جدید').press('Enter')
        assert await page.locator('.wo-form-modal .rte-editor').inner_text() == 'متن ثبت‌شده توسط کاربر'
        await page.locator('.wo-form-modal .wo-footer-actions .btn-primary').click()
        await page.locator('.wo-form-modal').wait_for(state='hidden')
        assert captured and captured[0]['title']=='آزمون ثبت فرم'
        assert captured[0]['recurrence']==1 and captured[0]['assigneeUserIds']==[2]
        assert captured[0]['checklistItems']==['مرحله تست'] and captured[0]['tags']==['بررسی']
        assert 'متن ثبت‌شده توسط کاربر' in captured[0]['description']
        await page.locator('.grid-desktop .wo-related-link').filter(has_text='WO/1405/102').click()
        await page.locator('#wo-detail-title').filter(has_text=b['title']).wait_for()
        await page.screenshot(path=str(ARTIFACTS/'wo-detail-desktop.png'),full_page=True)
        heading=await page.locator('.wo-detail-modal .wo-dialog-heading').bounding_box()
        actions=await page.locator('.wo-detail-modal .wo-header-actions').bounding_box()
        assert actions['x'] < heading['x'], 'Header actions must be left of the title'
        async def check_workflow_header():
            await page.locator('.wo-detail-modal .wo-header-actions button').filter(has_text='گردش کار').click()
            modal=page.locator('.wo-gardesh-modal')
            await modal.wait_for()
            head=await modal.locator('.wo-dialog-header').bounding_box()
            tools=await modal.locator('.wo-header-actions').bounding_box()
            close=await modal.locator('.btn-close').bounding_box()
            assert tools['x'] < head['x'] + 40, 'Workflow toolbar must be left-aligned'
            assert close['x'] < tools['x'] + tools['width']/2, 'Close button must stay at the left end'
            for label in ['لیستی','درختواره','فلوچارتی']:
                button=modal.get_by_role('button',name=label,exact=True)
                await button.click()
                assert 'btn-primary' in (await button.get_attribute('class'))
            await modal.get_by_role('button',name='بستن گردش کار',exact=True).click()
            await modal.wait_for(state='hidden')
            assert await page.locator('.wo-detail-modal').is_visible()
        await check_workflow_header()
        for text in ['گیرندگان','پیوست‌ها','گفت‌وگو','عملیات','اطلاعات']:
            await page.locator('.wo-detail-tabs button').filter(has_text=text).click()
            assert await page.locator('.wo-detail-body > section:visible').count()==1
        # Reopening the same linked number after closing must work even if the URL is unchanged.
        await page.locator('.wo-detail-modal .btn-close').click()
        await page.locator('.grid-desktop .wo-related-link').filter(has_text='WO/1405/102').click()
        await page.locator('#wo-detail-title').filter(has_text=b['title']).wait_for()
        await page.locator('.wo-detail-modal .wo-chain-link').filter(has_text='WO/1405/101').click()
        await page.locator('#wo-detail-title').filter(has_text=a['title']).wait_for()
        await page.locator('.wo-detail-modal .btn-close').click()
        await page.set_viewport_size(dict(width=390,height=844))
        await page.locator('.wo-page-action').click()
        await page.screenshot(path=str(ARTIFACTS/'wo-form-mobile.png'),full_page=True)
        footer=await page.locator('.wo-form-modal .modal-footer').bounding_box()
        assert footer['height'] < 100, footer
        assert await page.locator('.wo-form-modal').evaluate('(e) => e.scrollWidth <= e.clientWidth + 1')
        await page.locator('.wo-form-modal .modal-body').evaluate('(e) => e.scrollTop = e.scrollHeight')
        await page.locator('.wo-form-extras summary').filter(has_text='پیوست‌ها').click()
        assert await page.locator('.wo-form-modal input[type=file]').is_visible()
        await page.locator('.wo-form-modal .btn-close').click()
        await page.locator('.cards-mobile .wo-related-link').filter(has_text='WO/1405/102').click()
        await page.locator('#wo-detail-title').filter(has_text=b['title']).wait_for()
        await page.screenshot(path=str(ARTIFACTS/'wo-detail-mobile.png'),full_page=True)
        assert not errors, errors
        await check_workflow_header()
        print('PAGE ERRORS',errors)
        print('PASS: desktop/mobile forms, bidirectional list links, and all detail tabs.')
        await browser.close()
if __name__=='__main__':
    assert (CLIENT/'bin/Debug/net8.0/wwwroot/_framework/blazor.boot.json').is_file(), 'Build Inventory.Client first'
    threading.Thread(target=server.serve_forever,daemon=True).start()
    try: asyncio.run(main())
    finally: server.shutdown(); server.server_close()
