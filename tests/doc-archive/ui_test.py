"""UI regression check against real Blazor WASM with mock APIs; no production server/data required."""
import asyncio,json,os,threading
from pathlib import Path
from http.server import SimpleHTTPRequestHandler,ThreadingHTTPServer
from urllib.parse import unquote
from urllib.parse import urlsplit
from playwright.async_api import async_playwright, expect
CLIENT=Path(__file__).resolve().parents[2]/'inventory/src/Inventory.Client'
ARTIFACTS=Path(os.environ.get('DOC_UI_ARTIFACTS',str(Path.home()/'.cache/doc-archive-ui')))
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
permissions=['DocArchive.View','DocArchive.Create','DocArchive.Manage']
folders=[dict(id=1,name='اسناد فنی',myLevel=4,myCanDownload=True,documentCount=2),dict(id=2,name='قراردادها',myLevel=4,myCanDownload=True,documentCount=1)]
tags=[dict(id=1,name='فنی',color='#4f73bd')]
docs=[dict(id=i,code=f'DOC-{i:03}',customerCode=f'CL-{i:03}',title=title,folderId=1,folderName='اسناد فنی',isActive=True,myLevel=4,myCanDownload=True,activeVersionNo=2,versionCount=3,lastVersionStatus=2,tags=tags,expireDate='2026-12-01',daysToExpire=78,entityLinkCount=17,linkedModules=['LEGACY-ERP']) for i,title in enumerate(['نقشه تأسیسات ساختمان مرکزی','دستورالعمل کنترل کیفیت','قرارداد نگهداری تجهیزات'],1)]
async def main():
    async with async_playwright() as pw:
        browser=await pw.chromium.launch(headless=True,args=['--no-sandbox'])
        page=await browser.new_page(viewport=dict(width=1440,height=1000),device_scale_factor=1)
        errors=[]; erp_requests=[]; searches=[]; saved=[]; mode={'level':4}
        page.on('pageerror',lambda e:errors.append(str(e)))
        async def api(route):
            path=urlsplit(route.request.url).path.lower(); value={}
            if '/entity-links' in path or '/entity-lookups' in path: erp_requests.append(path)
            if '/hubs/' in path: return await route.fulfill(status=503,body='mock transport disabled')
            if path.endswith('/doc-archive/search-page'):
                f=route.request.post_data_json; searches.append(f)
                value=[dict(d,myLevel=mode['level']) for d in docs if f.get('search','') in d['title'] or f.get('search','') in d['code']]
                value=dict(items=value,total=len(value),page=1,pageSize=f.get('pageSize',25))
            elif path.endswith('/doc-archive/stats'): value=dict(inactive=0,deleted=0,expired=1,expiring=1)
            elif path.endswith('/doc-archive/folders'): value=[dict(f,myLevel=mode['level']) for f in folders]
            elif path.endswith('/doc-archive/tags'): value=tags
            elif path.endswith('/doc-archive/lookups'): value=dict(users=[dict(id=1,name='علی احمدی')],documents=[dict(id=1,name='نقشه تأسیسات')],roles=[],codeNumberingEnabled=True)
            elif path.endswith('/doc-archive/documents'):
                if route.request.method=='POST': saved.append(route.request.post_data_json); value=dict(id=20)
                else: value=[]
            elif path.endswith('/doc-archive/expiry-summary'): value=dict(expired=1,expiringSoon=1,expiringDays=60)
            elif 'code-settings' in path: value=dict(enabled=True,prefix='DOC-',padding=4,nextNumber=4)
            elif '/photo' in path: return await route.fulfill(status=404,body='')
            elif any(x in path for x in ['/notifications','/systeminfo','/chat/conversations']): value=[]
            await route.fulfill(content_type='application/json',body=json.dumps(value))
        await page.route('**/api/**',api); await page.route('**/hubs/**',api)
        async def session(perms,role):
            script='localStorage.setItem("authSession",'+json.dumps(json.dumps(dict(UserId=1,Token='ui-test',Username='test',Role=role,DisplayName='کاربر آزمایشی',Permissions=perms)))+');localStorage.setItem("apiBaseUrl",location.origin);'
            if page.url=='about:blank': await page.add_init_script('if(!localStorage.getItem("authSession")){'+script+'}')
            else: await page.evaluate(script)
        await session(permissions,'Admin')
        await page.goto(base+'/doc-archive/documents')
        await page.locator('.da-document-list tbody tr').first.wait_for(timeout=30000)
        # Verify the real scoped CSS bundle is served, not an HTML fallback.
        assert await page.locator('.da-page-header').evaluate("el => getComputedStyle(el).display")=='flex'
        assert await page.locator('.da-document-list thead th').count()==6
        assert await page.get_by_text('CL-001',exact=True).is_visible()
        heading=await page.locator('.da-page-heading').bounding_box(); actions=await page.locator('.da-page-actions').bounding_box()
        assert actions['x'] < heading['x']
        await page.screenshot(path=str(ARTIFACTS/'archive-desktop.png'),full_page=True)
        assert not await page.locator('.da-tools-menu').count()
        await page.locator('.da-tools-trigger').click()
        assert await page.locator('.da-tools-menu button').count()==6
        await page.locator('.da-tools-trigger').press('Escape')
        assert not await page.locator('.da-tools-menu').count()
        await page.locator('.da-tools-trigger').click()
        await page.locator('.da-tools-menu button').filter(has_text='دانلود ZIP').click()
        await page.locator('.modal.show').wait_for()
        assert await page.locator('.modal.show .alert-info').count()==0
        await page.locator('.modal.show .btn-close').click()
        await page.locator('.da-filter-button').click()
        await expect(page.locator('.da-filters')).not_to_contain_text('ERP')
        await expect(page.locator('body')).not_to_contain_text('LEGACY-ERP')
        await page.locator('.da-filters button').filter(has_text='PDF').click()
        await page.wait_for_function("document.querySelector('.da-filters button.btn-primary') != null")
        assert searches[-1]['fileTypes']==['pdf']
        await page.locator('.da-filters button').get_by_text('همه',exact=True).click()
        await page.locator('.da-document-list tbody tr').first.wait_for()
        assert searches[-1]['fileTypes']==[]
        await page.locator('.da-filter-button').click()
        await page.get_by_role('textbox',name='جستجوی اسناد',exact=True).fill('کنترل')
        await page.locator('.da-document-list tbody tr').filter(has_text='کنترل').wait_for()
        await expect(page.locator('.da-document-list tbody tr')).to_have_count(1)
        assert searches[-1]['search']=='کنترل'
        await page.get_by_role('button',name='پاک کردن جستجو',exact=True).click()
        await page.locator('.da-new-document').click()
        form=page.locator('.da-document-form .modal.show')
        await form.wait_for()
        assert await form.locator('details[open]').count()==0
        await page.screenshot(path=str(ARTIFACTS/'form-desktop.png'),full_page=True)
        await form.locator('#da-doc-title').fill('مدرک آزمون')
        await form.get_by_placeholder('انتخاب پوشه…').click()
        await form.locator('.ss-item').filter(has_text='اسناد فنی').click()
        await form.locator('#da-doc-description').fill('توضیحات واقعی مدرک')
        await form.locator('summary').get_by_text('نسخه و امنیت',exact=True).click()
        await form.locator('#dPub').check(); await form.locator('#dPubDl').check()
        await form.locator('#dConf').check(); await form.locator('#dWm').check()
        await form.locator('summary').get_by_text('نسخه و امنیت',exact=True).click()
        await form.locator('summary').get_by_text('برچسب‌ها و ارتباطات',exact=True).click()
        await form.locator('button.rounded-pill').filter(has_text='فنی').click()
        await form.locator('summary').get_by_text('برچسب‌ها و ارتباطات',exact=True).click()
        await form.locator('.modal-footer .btn-primary').click()
        await form.wait_for(state='hidden')
        assert saved and saved[-1]['title']=='مدرک آزمون' and saved[-1]['description']=='توضیحات واقعی مدرک'
        assert saved[-1]['isPublic'] and saved[-1]['publicCanDownload'] and saved[-1]['requireDownloadConfirm'] and saved[-1]['watermarkPreview']
        assert saved[-1]['tagIds']==[1] and saved[-1]['folderId']==1
        await page.locator('.da-new-document').click(); await form.wait_for()
        await expect(form.locator('#da-doc-title')).to_have_value('')
        assert await form.locator('details[open]').count()==0
        await form.locator('.btn-close').click()
        # Destructive actions still require explicit typed confirmation.
        await page.locator('.da-folders .ftree-item').filter(has_text='سطل بازیافت').click()
        await page.get_by_title('حذف قطعی و برگشت‌ناپذیر',exact=True).first.click()
        purge=page.locator('.modal.show')
        assert await purge.locator('.alert-danger').is_visible()
        assert await purge.locator('.modal-footer .btn-danger').is_disabled()
        await purge.get_by_placeholder('حذف قطعی',exact=True).fill('حذف قطعی')
        await purge.get_by_placeholder('حذف قطعی',exact=True).press('Tab')
        await expect(purge.locator('.modal-footer .btn-danger')).to_be_enabled()
        await purge.locator('.btn-close').click()
        # Legacy route remains valid; mobile defaults to cards with folders collapsed.
        await page.set_viewport_size(dict(width=390,height=844))
        await page.goto(base+'/doc-archive')
        await page.locator('.doc-cards-wrap .m-card').first.wait_for(timeout=30000)
        assert not await page.locator('.da-folders').is_visible()
        assert await page.evaluate('document.documentElement.scrollWidth <= innerWidth')
        await page.screenshot(path=str(ARTIFACTS/'archive-mobile.png'),full_page=True)
        await page.locator('.da-new-document').click(); await form.wait_for()
        await page.screenshot(path=str(ARTIFACTS/'form-mobile.png'),full_page=True)
        assert await page.evaluate('document.documentElement.scrollWidth <= innerWidth')
        await form.locator('.modal-footer .btn-primary').scroll_into_view_if_needed()
        await form.locator('.btn-close').click()
        # Read-only users keep ZIP/tags, but never see manager/create/purge actions.
        mode['level']=2
        await session(['DocArchive.View'],'Reader')
        await page.goto(base+'/doc-archive/documents')
        await page.locator('.doc-cards-wrap .m-card').first.wait_for(timeout=30000)
        assert await page.locator('.da-new-document').count()==0
        await page.locator('.da-tools-trigger').click()
        assert await page.locator('.da-tools-menu button').count()==2
        assert not errors,errors
        assert not erp_requests,erp_requests
        assert all(not f.get('linkedModule') and f.get('linkedEntityId') is None for f in searches)
        print('PASS: no ERP filters/badges/requests, archive routes, scoped styles, desktop/mobile, search/filter reset, tools, form payload, readonly controls; pageerrors=[]')
        await browser.close()
if __name__=='__main__':
    assert (CLIENT/'bin/Debug/net8.0/wwwroot/_framework/blazor.boot.json').is_file(),'Build Inventory.Client first'
    threading.Thread(target=server.serve_forever,daemon=True).start()
    try: asyncio.run(main())
    finally: server.shutdown(); server.server_close()
