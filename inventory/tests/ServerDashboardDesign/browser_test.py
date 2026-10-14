"""Visual regression checks for the standalone design preview (no live API)."""
from playwright.sync_api import sync_playwright, expect
from pathlib import Path
import json
import os
preview = os.environ.get('DASHBOARD_PREVIEW', '/home/user/server-dashboard-redesign.html')
out = Path(os.environ.get('DASHBOARD_SCREENSHOTS', '/home/user'))
checks=[]
with sync_playwright() as p:
    b=p.chromium.launch(args=['--no-sandbox'])
    page=b.new_page(viewport={'width':1366,'height':980},device_scale_factor=1)
    errors=[]
    page.on('pageerror',lambda e:errors.append(str(e)))
    page.goto(Path(preview).as_uri()); page.evaluate('document.fonts.ready')
    expect(page.locator('#empty .infra-metric')).to_have_count(8)
    checks.append('Eight empty-state cards')
    page.screenshot(path=str(out/'server-dashboard-desktop.png'),full_page=True)
    page.get_by_role('button',name='با دادهٔ نمونه',exact=True).click()
    expect(page.locator('#example')).to_be_visible(); checks.append('Sample data view')
    page.locator('#example .infra-metric').nth(3).click()
    expect(page.locator('#example .infra-metric').nth(3)).to_have_attribute('aria-pressed','true'); checks.append('Selected filter visible')
    page.locator('#example .infra-auto-refresh').click()
    expect(page.locator('#example .infra-auto-refresh input')).not_to_be_checked(); checks.append('Integrated toggle works via label')
    page.locator('#example .infra-auto-refresh input').focus();page.keyboard.press('Space')
    expect(page.locator('#example .infra-auto-refresh input')).to_be_checked(); checks.append('Toggle is keyboard accessible')
    page.locator('#example summary').click()
    expect(page.locator('#example details')).to_have_attribute('open',''); checks.append('Explanation expands accessibly')
    page.get_by_role('button',name='شرکت بدون سرور',exact=True).click()
    for width in [320,390,768,1024]:
        page.set_viewport_size({'width':width,'height':950})
        assert page.evaluate('document.documentElement.scrollWidth <= innerWidth'),f'overflow {width}'
        checks.append(f'No document overflow at {width}px')
        if width==390: page.screenshot(path=str(out/'server-dashboard-mobile.png'),full_page=True)
    assert not errors,errors
    checks.append('No JavaScript errors')
    b.close()
Path(__file__).with_name('RESULTS.json').write_text(json.dumps({'razor_render_assertions':8,'browser_checks':checks,'browser_test_scope':'Standalone HTML rendered from production Razor component; preview-only interaction handlers, not live API.'},ensure_ascii=False,indent=2))
print('PASS:',len(checks),'browser checks')
