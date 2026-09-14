"""Real compiled Blazor UI; mocked APIs, never production data."""
import asyncio,importlib.util,json,threading
from pathlib import Path
from urllib.parse import urlsplit
from playwright.async_api import async_playwright,expect
spec=importlib.util.spec_from_file_location('archive_ui',Path(__file__).resolve().parents[1]/'doc-archive/ui_test.py')
fixture=importlib.util.module_from_spec(spec);spec.loader.exec_module(fixture)
ART=Path.home()/'.cache/archive-evolution-ui';ART.mkdir(parents=True,exist_ok=True)
async def main():
    async with async_playwright() as pw:
        browser=await pw.chromium.launch(headless=True,args=['--no-sandbox'])
        page=await browser.new_page(viewport=dict(width=1440,height=1000))
        errors=[];erp_requests=[];captured=[];grants=[];searches=[];stats=[]
        page.on('pageerror',lambda e:errors.append(str(e)))
        versions=[dict(id=i,documentId=1,versionNo=n,createdAt='2026-09-14',createdByName='آزمایش',status=2,isActive=True,attachmentCount=1) for i,n in [(11,1),(12,2)]]
        async def api(route):
            path=urlsplit(route.request.url).path;method=route.request.method;value=[]
            if '/entity-links' in path or '/entity-lookups' in path: erp_requests.append(path)
            if '/hubs/' in path:return await route.fulfill(status=503,body='mock')
            if path.endswith('/doc-archive/documents/1'):value=dict(fixture.docs[0],versions=versions,description='مدرک آزمایشی',entityLinks=[dict(id=17,documentId=1,module='Projects',entityId=77,entityTitle='LEGACY-ERP-TITLE')])
            elif path.endswith('/doc-archive/lookups'):value=dict(users=[dict(id=1,name='کاربر آزمایشی'),dict(id=2,name='مسئول تمدید')],roles=[],documents=[],codeNumberingEnabled=True)
            elif path.endswith('/doc-archive/folders'):value=fixture.folders
            elif path.endswith('/doc-archive/tags'):value=fixture.tags
            elif path.endswith('/temporary-grants'):
                if method=='POST':
                    payload=route.request.post_data_json;captured.append(('grant',payload));grants[:]=[dict(payload,id=1,userName='مسئول تمدید')];value=dict(id=1)
                else:value=grants
            elif '/temporary-grants/' in path:
                captured.append(('revoke',path));grants.clear();value={}
            elif path.endswith('/renewal'):
                if method=='PUT':captured.append(('renewal',route.request.post_data_json));value={}
                else:value=dict(enabled=False,assigneeUserId=2,leadDays=30,orders=[])
            elif '/compare-files/' in path:value=[dict(id=101 if path.endswith('/11') else 102,name='قرارداد.txt')]
            elif path.endswith('/compare-content'):
                captured.append(('compare',route.request.post_data_json));value=dict(leftName='قرارداد.txt',rightName='قرارداد.txt',mode='Text',rows=[dict(field='۱',left='مبلغ ۱۰۰',right='مبلغ ۲۰۰',kind='changed'),dict(field='۲',left='<script>bad()</script>',right='<script>bad()</script>',kind='same')])
            elif path.endswith('/compare'):value=dict(left=versions[0],right=versions[1],fields=[],approvers=[],attachments=[],between=[],changeCount=0)
            elif path.endswith('/doc-archive/stats'):stats.append(path);value=dict(inactive=0,deleted=0,expired=0,expiring=0)
            elif path.endswith('/doc-archive/search-page'):
                f=route.request.post_data_json;searches.append(f)
                if f.get('search')=='old':await asyncio.sleep(.8)
                doc=dict(fixture.docs[0],id=f['page'],title=f.get('search') or 'مدرک صفحه '+str(f['page']))
                value=dict(items=[doc],total=60,page=f['page'],pageSize=f['pageSize'])
            elif path.endswith('/index-queue'):value=dict(pending=2,working=1,failed=1,done=5,failures=[dict(attachmentId=101,fileName='فایل ناموفق.pdf',error='خطای پردازش')])
            elif path.endswith('/retry'):captured.append(('retry',path));value={}
            elif '/photo' in path:return await route.fulfill(status=404,body='')
            await route.fulfill(content_type='application/json',body=json.dumps(value))
        await page.route('**/api/**',api);await page.route('**/hubs/**',api)
        auth=dict(UserId=1,Token='mock',Username='test',Role='Admin',DisplayName='آزمایش',Permissions=['DocArchive.Read','DocArchive.Manage','DocArchive.Create','WorkOrders.View','WorkOrders.Create','WorkOrders.AssignOthers'])
        await page.add_init_script('localStorage.setItem("authSession",'+json.dumps(json.dumps(auth))+');localStorage.setItem("apiBaseUrl",location.origin);')
        await page.goto(fixture.base+'/doc-archive/documents/1')
        await page.get_by_role('button').filter(has_text='دسترسی موقت').wait_for(timeout=30000)
        async def dialogs(mobile=False):
            await expect(page.locator('body')).not_to_contain_text('اتصالات سامانه ERP')
            await expect(page.locator('body')).not_to_contain_text('LEGACY-ERP-TITLE')
            await page.get_by_role('button').filter(has_text='دسترسی موقت').click()
            modal=page.locator('.modal.show');await modal.wait_for()
            await modal.locator('.ss input').click();await modal.locator('.ss-item').filter(has_text='مسئول تمدید').click()
            await modal.get_by_label('اجازه دانلود').check()
            await modal.get_by_role('button',name='ثبت دسترسی',exact=True).click()
            await expect(modal.locator('tbody tr')).to_have_count(1)
            assert captured[-1][0]=='grant' and captured[-1][1]['userId']==2 and captured[-1][1]['expiresAtUtc'].endswith('Z')
            await page.screenshot(path=str(ART/('grant-mobile.png' if mobile else 'grant-desktop.png')),full_page=True)
            await modal.get_by_role('button',name='لغو',exact=True).click();await expect(modal.locator('tbody tr')).to_have_count(0)
            await modal.locator('.btn-close').click()
            await page.get_by_role('button').filter(has_text='پیگیری تمدید').click();await modal.wait_for()
            await modal.get_by_label('فعال',exact=True).check();await modal.locator('#renewal-days').fill('15')
            await modal.get_by_role('button',name='ذخیره',exact=True).click();await modal.wait_for(state='hidden')
            assert captured[-1][0]=='renewal' and captured[-1][1]['leadDays']==15 and captured[-1][1]['enabled']
            await page.get_by_title('مقایسه دو ورژن',exact=True).click();await modal.wait_for()
            await expect(modal.locator('#compare-left')).to_have_value('101');await expect(modal.locator('#compare-right')).to_have_value('102')
            await modal.get_by_role('button',name='مقایسه',exact=True).click()
            await expect(modal.locator('.table-success')).to_have_text('مبلغ ۲۰۰')
            assert captured[-1][1]==dict(leftAttachmentId=101,rightAttachmentId=102)
            await modal.get_by_label('فقط تفاوت‌ها',exact=True).uncheck()
            await expect(modal.locator('td').filter(has_text='<script>bad()</script>')).to_have_count(2)
            await page.screenshot(path=str(ART/('compare-mobile.png' if mobile else 'compare-desktop.png')),full_page=True)
            assert await page.evaluate('document.documentElement.scrollWidth <= innerWidth')
            await modal.locator('.btn-close').click()
        await dialogs()
        await page.set_viewport_size(dict(width=390,height=844));await dialogs(True)
        await page.goto(fixture.base+'/doc-archive/documents');await page.locator('.da-search input').wait_for()
        pager=page.get_by_role('navigation',name='صفحه‌بندی مدارک')
        await pager.get_by_role('button',name='بعدی',exact=True).click();await expect(page.locator('.da-result-count')).to_contain_text('۶۰')
        assert searches[-1]['page']==2
        before=len(stats);count=len(searches)
        await page.locator('.da-search input').press_sequentially('test',delay=20)
        await expect(page.locator('.da-document-title')).to_have_text('test')
        assert len(searches)==count+1 and searches[-1]['page']==1 and len(stats)==before
        # Old responses cannot overwrite a more recent search.
        await page.locator('.da-search input').fill('old');await page.wait_for_timeout(450)
        await page.locator('.da-search input').fill('new');await expect(page.locator('.da-document-title')).to_have_text('new')
        await page.wait_for_timeout(800);await expect(page.locator('.da-document-title')).to_have_text('new')
        await page.locator('.da-tools-trigger').click();await page.locator('.da-tools-menu button').filter(has_text='صف پردازش').click()
        await expect(page.locator('.modal.show')).to_contain_text('فایل ناموفق.pdf')
        await page.get_by_role('button',name='تلاش مجدد',exact=True).click()
        assert captured[-1][0]=='retry'
        assert not erp_requests,erp_requests
        assert all(not f.get('linkedModule') and f.get('linkedEntityId') is None for f in searches)
        assert not errors,errors
        print('PASS: no ERP card/actions/requests, desktop/mobile temporary grants/revoke, renewal payload, real content diff controls, paging, debounce, stale-response protection, queue retry; no page errors')
        await browser.close()
if __name__=='__main__':
    threading.Thread(target=fixture.server.serve_forever,daemon=True).start()
    try:asyncio.run(main())
    finally:fixture.server.shutdown();fixture.server.server_close()
