"""Compiled Blazor attachment downloads; mocked APIs and simulated popup blocking.
Checks actual saved bytes, filenames, authorization, errors and password expiry.
"""
import asyncio, importlib.util, json, threading
from pathlib import Path
from urllib.parse import quote, urlsplit
from playwright.async_api import async_playwright, expect

spec = importlib.util.spec_from_file_location('archive_ui', Path(__file__).resolve().parents[1] / 'doc-archive/ui_test.py')
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)
ART = Path.home() / '.cache/archive-download-ui'
ART.mkdir(parents=True, exist_ok=True)
CONTENT = 'محتوای واقعی فایل آزمایشی\n'.encode()
NAME = 'سند نهایی.txt'

async def main():
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(headless=True, args=['--no-sandbox'])
        page = await browser.new_page(viewport=dict(width=1440, height=1000), accept_downloads=True)
        errors, requests, downloads, confirms = [], [], [], []
        state = dict(mode='success', confidential=False, confirmed=False, allow=True, delay=0)
        page.on('pageerror', lambda e: errors.append(str(e)))
        page.on('download', lambda d: downloads.append(d))
        version = dict(id=11, documentId=1, versionNo=1, createdAt='2026-09-14', createdByName='آزمایش', status=2, isActive=True, attachmentCount=1)
        async def api(route):
            req = route.request
            path = urlsplit(req.url).path.lower()
            value = []
            if '/hubs/' in path: return await route.fulfill(status=503, body='mock')
            if path.endswith('/attachments/download/101'):
                requests.append(req)
                await asyncio.sleep(state['delay'])
                mode = state['mode']
                if mode == 'network': return await route.abort('failed')
                if mode == 'html': return await route.fulfill(content_type='text/html', body='<html>SPA fallback</html>')
                if mode == 'error': return await route.fulfill(status=403, json=dict(message='شما اجازه دانلود این فایل را ندارید؛ فقط امکان مشاهده دارید.'))
                if mode == 'missing': return await route.fulfill(status=404, body='')
                if mode == 'unauthorized': return await route.fulfill(status=401, body='')
                if state['confidential'] and not state['confirmed']:
                    return await route.fulfill(status=403, json=dict(code='PASSWORD_CONFIRM_REQUIRED', documentId=1, message='تایید رمز لازم است.'))
                return await route.fulfill(content_type='text/plain; charset=utf-8', headers={'Content-Disposition': "attachment; filename=fallback.txt; filename*=UTF-8''" + quote(NAME)}, body=CONTENT)
            if path.endswith('/doc-archive/documents/1/confirm-download'):
                confirms.append(req.post_data_json)
                if req.post_data_json['password'] != 'correct-test-password':
                    return await route.fulfill(status=400, json=dict(message='رمز عبور نادرست است.'))
                # The download must survive an asynchronous confirmation, not just a direct click.
                await asyncio.sleep(1.0)
                state['confirmed'] = True
                value = {}
            elif path.endswith('/doc-archive/documents/1'):
                value = dict(fixture.docs[0], versions=[version], description='آزمون دانلود')
            elif path.endswith('/attachments/docversion/11'):
                value = [dict(id=101, fileName='نام اولیه.txt', size=len(CONTENT), contentType='text/plain', uploadedAt='2026-09-14', uploaderName='آزمایش', canPreview=True, canDownload=state['allow'], documentId=1 if state['confidential'] else 0, needsConfirm=state['confidential'] and not state['confirmed'])]
            elif path.endswith('/attachments/preview/101'):
                return await route.fulfill(content_type='text/plain; charset=utf-8', body=CONTENT)
            elif path.endswith('/doc-archive/lookups'):
                value = dict(users=[], roles=[], documents=[], codeNumberingEnabled=True)
            elif path.endswith('/doc-archive/folders'): value = fixture.folders
            elif path.endswith('/doc-archive/tags'): value = fixture.tags
            elif path.endswith('/renewal'): value = dict(enabled=False, leadDays=30, orders=[])
            await route.fulfill(content_type='application/json', body=json.dumps(value))
        await page.route('**/api/**', api)
        await page.route('**/hubs/**', api)
        auth = dict(UserId=1, Token='download-test-token', Username='test', Role='Admin', DisplayName='آزمایش', Permissions=['DocArchive.Read','DocArchive.Manage'])
        await page.add_init_script('localStorage.setItem("authSession",' + json.dumps(json.dumps(auth)) + ');localStorage.setItem("apiBaseUrl",location.origin);window.popupAttempts=0;window.open=()=>{window.popupAttempts++;return null;};')
        button = page.locator('.attx-actions button[title="دانلود"]')
        async def load():
            await page.goto(fixture.base + '/doc-archive/documents/1')
            await page.locator('.attx-row').wait_for(timeout=30000)
        async def saved(action, name):
            async with page.expect_download() as event:
                await action()
            download = await event.value
            assert download.suggested_filename == NAME, download.suggested_filename
            await download.save_as(ART / name)
            assert (ART / name).read_bytes() == CONTENT
            assert len(page.context.pages) == 1
            assert await page.evaluate('window.popupAttempts') == 0
            await expect(button).to_be_enabled()
        await load()
        await saved(button.click, 'desktop.txt')
        # No second request while downloading and clear busy feedback.
        state['delay'] = .7
        before = len(requests)
        async with page.expect_download() as event:
            await button.click()
            await expect(button).to_be_disabled()
            await button.evaluate('(el)=>el.click()')
        await (await event.value).save_as(ART / 'busy.txt')
        await expect(button).to_be_enabled()
        assert len(requests) == before + 1
        state['delay'] = 0
        for mode, message in [('error','فقط امکان مشاهده دارید'), ('missing','فایل در سرور یافت نشد'), ('unauthorized','نشست شما منقضی شده'), ('html','پاسخ نامعتبر'), ('network','اتصال شبکه')]:
            state['mode'] = mode
            before = len(downloads)
            await button.click()
            await expect(page.locator('.toast-item').filter(has_text=message).last).to_be_visible()
            await expect(button).to_be_enabled()
            assert len(downloads) == before
        state['mode'] = 'success'
        await page.locator('.attx-actions button[title="پیش‌نمایش"]').click()
        await page.locator('.fpv-text').wait_for()
        await saved(page.get_by_title('دانلود فایل', exact=True).click, 'preview.txt')
        # Expire password confirmation while the preview is open: ask again, never bypass.
        state['confidential'] = True
        await page.get_by_title('دانلود فایل', exact=True).click()
        modal = page.locator('.modal.show')
        await expect(modal).to_contain_text('تایید مجدد رمز')
        await expect(page.locator('.fpv-dialog')).to_have_count(0)
        count = len(requests)
        await modal.locator('input[type=password]').fill('wrong')
        await modal.get_by_role('button', name='تایید رمز', exact=True).click()
        await expect(modal).to_contain_text('رمز عبور نادرست است')
        assert len(requests) == count
        await modal.locator('input[type=password]').fill('correct-test-password')
        await saved(modal.get_by_role('button', name='تایید رمز', exact=True).click, 'confirmed.txt')
        await expect(modal).to_have_count(0)
        # ConfirmedDocs must not suppress future server-side expiry challenges.
        state['confirmed'] = False
        await button.click()
        await expect(modal).to_contain_text('تایید مجدد رمز')
        await modal.get_by_role('button', name='انصراف', exact=True).click()
        # Mobile with an initially locked attachment.
        await page.set_viewport_size(dict(width=390, height=844))
        await load()
        before = len(requests)
        await button.click()
        await expect(modal).to_contain_text('تایید مجدد رمز')
        assert len(requests) == before
        await modal.locator('input[type=password]').fill('correct-test-password')
        await saved(modal.get_by_role('button', name='تایید رمز', exact=True).click, 'mobile-confirmed.txt')
        await page.screenshot(path=str(ART / 'mobile.png'), full_page=True)
        # View-only access still has no download control, including the preview.
        state['allow'] = False
        await load()
        await expect(button).to_have_count(0)
        await page.locator('.attx-actions button[title="پیش‌نمایش"]').click()
        await page.locator('.fpv-text').wait_for()
        await expect(page.get_by_title('دانلود فایل', exact=True)).to_have_count(0)
        assert all(r.headers.get('authorization') == 'Bearer download-test-token' for r in requests)
        assert all(not urlsplit(r.url).query for r in requests)
        assert not errors, errors
        print('PASS: actual desktop/mobile downloaded bytes and Persian filename; no popups/token URLs; preview download; busy guard; 401/403/404/network/HTML errors; password retry/expiry/confirmation; view-only controls; no pageerrors')
        await browser.close()

if __name__ == '__main__':
    threading.Thread(target=fixture.server.serve_forever, daemon=True).start()
    try: asyncio.run(main())
    finally: fixture.server.shutdown(); fixture.server.server_close()
