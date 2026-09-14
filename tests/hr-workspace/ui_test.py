"""Real compiled Blazor with mocked API/auth. No production database or device claims."""
import asyncio, importlib.util, json, threading
from pathlib import Path
from urllib.parse import urlsplit, parse_qs
from playwright.async_api import async_playwright, expect
spec=importlib.util.spec_from_file_location('archive_ui',Path(__file__).resolve().parents[1]/'doc-archive/ui_test.py')
fixture=importlib.util.module_from_spec(spec);spec.loader.exec_module(fixture)
ART=Path.home()/'.cache/hr-ui';ART.mkdir(parents=True,exist_ok=True)
EMP=dict(id=42,code='E042',firstName='علی',lastName='احمدی',nationalCode='1234567890',hireDate='2026-01-01',isActive=True,hrMainNodeId=2,hrMainNodeName='فنی',hrMainPositionId=9,hrMainPositionTitle='کارشناس',managerId=500,managerName='مدیر ثبت‌شده')
async def main():
 async with async_playwright() as pw:
  browser=await pw.chromium.launch(headless=True,args=['--no-sandbox'])
  page=await browser.new_page(viewport=dict(width=1440,height=1000))
  errors=[];requests=[];saved=[];state=dict(overviewFail=False,listFail=False,employeeFail=False,dossierFail=False,empty=False)
  page.on('pageerror',lambda e:errors.append(str(e)))
  async def api(route):
   u=urlsplit(route.request.url);p=u.path.lower();requests.append(p);v=[]
   if '/hubs/' in p:return await route.fulfill(status=503,body='mock transport disabled')
   if p.endswith('/hr-main/overview'):
    if state['overviewFail']:return await route.fulfill(status=503,json=dict(message='test unavailable'))
    v=dict(companyName='' if state['empty'] else 'شرکت آزمایشی',activeEmployees=0 if state['empty'] else 55,activeNodes=0 if state['empty'] else 2,activePositions=0 if state['empty'] else 4,activeBranches=1,holidaysThisYear=0,currentJalaliYear=1405)
   elif p.endswith('/hr-main/org/nodes'):v=[dict(id=1,name='اداری',level=0),dict(id=2,name='فنی',level=1)]
   elif p.endswith('/hr-main/positions'):v=[dict(id=9,title='کارشناس',orgNodeId=2)]
   elif p.endswith('/hr-core/employees'):
    if route.request.method=='POST':saved.append(route.request.post_data_json);v={**EMP,**route.request.post_data_json,"id":77}
    else:
     if state['listFail']:return await route.fulfill(status=503,json=dict(message='test unavailable'))
     q=parse_qs(u.query);v=dict(items=[EMP],total=27 if q.get('hrMainNodeId')==['2'] else 55)
   elif p.endswith('/hr-core/employees/42'):
    if state['employeeFail']:return await route.fulfill(status=503,json=dict(message='test unavailable'))
    if route.request.method=='PUT':saved.append(route.request.post_data_json)
    v=EMP
   elif '/employees/' in p and p.endswith('/dossier'):
    if state['dossierFail']:return await route.fulfill(status=503,json=dict(message='test unavailable'))
    v=dict(employee=EMP,dependents=[],courses=[],skills=[],languages=[],documents=[],expiringDocuments=[])
   elif p.endswith('/hr-main/company'):v=dict(name='شرکت آزمایشی',id=1)
   elif p.endswith('/unread-count'):v=dict(count=0)
   elif p.endswith('/push-status'):v=dict(configured=False,registered=False)
   await route.fulfill(json=v)
  await page.route('**/api/**',api);await page.route('**/hubs/**',api)
  async def session(perms=[],role='Admin'):
   script='localStorage.setItem("authSession",'+json.dumps(json.dumps(dict(UserId=1,Token='hr-ui-test',Username='test',Role=role,DisplayName='آزمایش',Permissions=perms)))+');localStorage.setItem("apiBaseUrl",location.origin);'
   if page.url=='about:blank':await page.add_init_script('if(!localStorage.getItem("authSession")){'+script+'}')
   else:await page.evaluate(script)
  async def go(path):
   await page.goto(fixture.base+'/'+path)
   await page.locator('.hr-workspace').wait_for(timeout=60000)
  async def no_overflow():assert await page.evaluate('document.documentElement.scrollWidth<=innerWidth+1')
  try:
   await session();await go('hr-main')
   await expect(page.locator('.hr-stat-grid strong')).to_have_count(4)
   await expect(page.locator('.hr-area')).to_have_count(7)
   await expect(page.locator('.hr-setup')).to_have_count(0)
   assert await page.locator('.hr-section-grid').evaluate('e=>getComputedStyle(e).display')=='grid'
   heading=await page.locator('.hr-home-identity').bounding_box();actions=await page.locator('.hr-home-actions').bounding_box();assert actions['x']<heading['x']
   nav=page.locator('.hr-navigation');await expect(nav).to_have_count(1)
   await expect(nav.locator('.hr-nav-children')).to_have_count(0)
   await nav.get_by_role('button',name='پرسنل و کارگزینی',exact=True).click();await expect(nav.locator('.hr-nav-children')).to_have_count(1)
   await nav.get_by_role('button',name='حضور و مرخصی',exact=True).click();await expect(nav.locator('.hr-nav-children')).to_have_count(1)
   # Restore the original app theme; only HR receives the simplified local styles.
   sidebar=page.locator('.app-sidebar')
   assert 'linear-gradient' in await sidebar.evaluate("e=>getComputedStyle(e).backgroundImage")
   assert await sidebar.locator('.logo').evaluate("e=>getComputedStyle(e).animationName")=='brandGlow'
   assert await page.locator('.group-dash .nav-section-title').evaluate("e=>getComputedStyle(e).color")=='rgb(34, 211, 238)'
   await expect(page.locator('link[href="css/sidebar.css"]')).to_have_count(0)
   active=nav.locator('a.active')
   assert await active.evaluate("e=>getComputedStyle(e).backgroundImage")=='none'
   assert await active.evaluate("e=>getComputedStyle(e).boxShadow")=='none'
   assert await nav.locator('.nav-section-title').evaluate("e=>getComputedStyle(e,'::before').display")=='none'
   assert await nav.locator('.hr-nav-children a').first.evaluate("e=>getComputedStyle(e).color")=='rgb(203, 213, 225)'
   await nav.get_by_role('button',name='حضور و مرخصی',exact=True).focus();await page.keyboard.press('Tab');await page.keyboard.press('Shift+Tab')
   assert await nav.get_by_role('button',name='حضور و مرخصی',exact=True).evaluate("e=>getComputedStyle(e).outlineStyle")=='solid'
   await page.locator('.hr-home-identity').click()
   await no_overflow();await page.screenshot(path=str(ART/'home-desktop.png'),full_page=True)
   await sidebar.screenshot(path=str(ART/'menu-desktop.png'))
   await page.locator('.hr-area[href="hr-main/section/people"]').click();await expect(page.locator('.hr-link-card')).to_have_count(10)
   await page.locator('.hr-link-card[href="hr-main/employees"]').click();await expect(page.locator('.hr-workspace tbody tr')).to_have_count(1)
   await page.get_by_role('combobox',name='فیلتر واحد سازمانی').select_option('2')
   await expect(page.locator('.hr-workspace')).to_contain_text('۲۷')
   await page.locator('.hr-workspace a[href="hr-main/employees/42/profile"]').first.click()
   await expect(page.locator('.hr-workspace a[href="hr-main/employees/42"]')).to_have_count(1)
   await page.locator('.hr-workspace a[href="hr-main/employees/42"]').click()
   await expect(page.get_by_role('heading',name='ویرایش پرونده پرسنل')).to_be_visible()
   for _ in range(4):await page.get_by_role('button',name='مرحله بعد').click()
   await page.locator('button').filter(has_text='ثبت تغییرات').click();await page.wait_for_url('**/hr-main/employees/42/profile')
   assert saved[-1]['hrMainNodeId']==2 and saved[-1]['managerId']==500
   await page.locator('.hr-workspace a[href="hr-main/employees"]').first.click();await page.wait_for_url('**/hr-main/employees')
   await go('hr-core/employees/42');await expect(page.locator('.hr-workspace a[href="hr-core/employees"]')).to_have_count(2)
   # Error is not an empty organization; retry performs a fresh successful request.
   state['overviewFail']=True;await go('hr-main');await expect(page.locator('.hr-error')).to_be_visible();await expect(page.locator('.hr-stat-grid')).to_have_count(0);await expect(page.locator('.hr-setup')).to_have_count(0)
   state['overviewFail']=False;await page.get_by_role('button',name='تلاش مجدد',exact=True).click();await expect(page.locator('.hr-stat-grid')).to_be_visible()
   state['empty']=True;await go('hr-main');await expect(page.locator('.hr-setup')).to_be_visible();state['empty']=False
   state['listFail']=True;await go('hr-main/employees');await expect(page.locator('.hr-error')).to_be_visible()
   state['listFail']=False;await page.get_by_role('button',name='تلاش مجدد',exact=True).click();await expect(page.locator('.hr-workspace tbody tr')).to_have_count(1)
   state['employeeFail']=True;await go('hr-main/employees/42');await expect(page.locator('.hr-error')).to_be_visible();await expect(page.locator('.wiz-steps')).to_have_count(0)
   state['employeeFail']=False;await page.get_by_role('button',name='تلاش مجدد',exact=True).click();await expect(page.locator('.wiz-steps')).to_be_visible()
   state['dossierFail']=True;await go('hr-main/employees/42/profile');await expect(page.locator('.hr-error')).to_be_visible();await expect(page.locator('.hr-workspace')).not_to_contain_text('پرسنل یافت نشد')
   state['dossierFail']=False;await page.get_by_role('button',name='تلاش مجدد',exact=True).click();await expect(page.locator('.hr-workspace a[href="hr-main/employees/42"]')).to_be_visible()
   # Shared company header buttons on the left, desktop and responsive home/menu.
   await go('hr-main/company');await expect(page.locator('button').filter(has_text='ذخیره اطلاعات شرکت')).to_be_visible()
   h=await page.locator('.page-head h1').bounding_box();a=await page.locator('.page-head .ms-auto').bounding_box();assert a['x']<h['x']
   await page.screenshot(path=str(ART/'company-desktop.png'),full_page=True)
   await page.set_viewport_size(dict(width=390,height=844));await go('hr-main');await expect(page.locator('.hr-area')).to_have_count(7);await no_overflow();await page.screenshot(path=str(ART/'home-mobile.png'),full_page=True)
   await page.get_by_title('باز/بسته کردن منو',exact=True).click();await expect(nav).to_be_visible()
   await page.wait_for_function("['none','matrix(1, 0, 0, 1, 0, 0)'].includes(getComputedStyle(document.querySelector('.app-sidebar')).transform)")
   await expect(page.locator('.app-bottom-nav')).to_be_visible()
   await expect(page.locator('.sidebar-close')).to_have_count(0)
   await page.screenshot(path=str(ART/'menu-mobile.png'))
   assert await sidebar.evaluate('e=>e.scrollWidth<=e.clientWidth+1')
   for box in await nav.locator('button').all():
    assert (await box.bounding_box())['height']>=44
   await nav.get_by_role('button',name='پرسنل و کارگزینی',exact=True).click();await nav.locator('a[href="hr-main/employees"]').click();await expect(page.locator('.app-shell')).not_to_have_class(__import__('re').compile(r'.*sidebar-open.*'));await no_overflow()
   await go('hr-main/employees/42');await expect(page.locator('.wiz-steps')).to_be_visible();await no_overflow();await page.screenshot(path=str(ART/'wizard-mobile.png'),full_page=True)
   await page.set_viewport_size(dict(width=1440,height=1000))
   # Read/create/manage are independent. No overview call without HrMain.Read.
   await session(['FaLms.Manage'],'Viewer');requests.clear();await go('hr-main');await expect(page.locator('.hr-area')).to_have_count(3);assert '/api/hr-main/overview' not in requests
   await go('hr-main/settings');await expect(page.locator('.hr-link-card')).to_have_count(1);await expect(page.locator('.hr-link-card')).to_contain_text('بانک سؤال')
   await session(['HrCore.Read','HrCore.Update'],'Viewer');requests.clear();await go('hr-main/employees/42');await expect(page.locator('.wiz-steps')).to_be_visible()
   for _ in range(2):await page.get_by_role('button',name='مرحله بعد').click()
   organization=page.locator('.hr-workspace select').nth(0);await expect(organization).to_be_disabled();await expect(organization).to_have_value('2')
   for _ in range(2):await page.get_by_role('button',name='مرحله بعد').click()
   await page.locator('button').filter(has_text='ثبت تغییرات').click();await page.wait_for_url('**/hr-main/employees/42/profile');assert saved[-1]['hrMainNodeId']==2 and saved[-1]['hrMainPositionId']==9 and saved[-1]['managerId']==500
   assert '/api/hr-main/org/nodes' not in requests
   await session(['FaAtt.Create'],'Viewer');await go('hr-main');await expect(page.locator('.hr-area')).to_have_count(1);await expect(page.locator('.hr-primary')).to_contain_text('کارتابل من')
   await session(['HrCore.Create'],'Viewer');requests.clear();await go('hr-main/employees/new');await expect(page.locator('.wiz-steps')).to_be_visible();assert '/api/hr-main/org/nodes' not in requests and '/api/hr-core/employees' not in requests
   inputs=page.locator('.hr-workspace .card-body input.form-control');await inputs.nth(0).fill('مریم');await inputs.nth(1).fill('رضایی');await inputs.nth(2).fill('1234567890');await inputs.nth(2).blur()
   for _ in range(4):await page.get_by_role('button',name='مرحله بعد').click()
   await page.locator('button').filter(has_text='ثبت نهایی پرسنل').click();await page.wait_for_url('**/hr-main');assert saved[-1]['firstName']=='مریم'
   # HR title is scoped to its routes, not written into the global LayoutState.
   await session();await go('hr-main');await page.evaluate("Blazor.navigateTo('/bon-hr')");await page.wait_for_url('**/bon-hr');await expect(page.locator('.hr-workspace')).to_have_count(0);await expect(page.locator('.topbar-title')).not_to_contain_text('خانه منابع انسانی')
   await session([],'Viewer');await go('hr-main');await expect(page.locator('.hr-navigation')).to_have_count(0);await expect(page.locator('.hr-workspace')).to_contain_text('دسترسی منابع انسانی')
   assert not errors,errors
   print('PASS: compiled desktop/mobile home, accordion, section, employee list/profile/edit/save/back, legacy back, server-filter query, loading failure/retry, empty setup, role isolation, create-only save, left actions, responsive width; no pageerrors.')
  except Exception:
   await page.screenshot(path=str(ART/'failure.png'),full_page=True)
   print('ERRORS',errors,'BODY', (await page.locator('body').inner_text())[-9000:]);raise
  finally:await browser.close()
if __name__=='__main__':
 threading.Thread(target=fixture.server.serve_forever,daemon=True).start()
 try:asyncio.run(main())
 finally:fixture.server.shutdown();fixture.server.server_close()
