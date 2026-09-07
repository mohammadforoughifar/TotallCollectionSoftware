#!/usr/bin/env python3
"""PWA/service-worker download scenarios (the 'new tab shows the app, only Ctrl+Shift+R downloads' bug).

Regression scenarios:
1. File download button downloads in the SAME page (no extra tab), with the SW active.
2. Clicking an image opens an in-page lightbox (no new tab) whose download button works in place.
3. The service worker never answers /api|/hubs|access_token URLs with the cached app shell:
   fetches/navigations to download & preview URLs reach the server.
4. The SW still serves the app shell for ordinary non-API navigations (guard is targeted).

Run against a disposable published app: CHAT_TEST_DISPOSABLE=1 python3 tests/chat/browser_pwa_download_test.py
"""
import asyncio, base64, json, sys
sys.path.insert(0, '.')
from pathlib import Path
from playwright.async_api import async_playwright, expect
from realtime_files_test import BASE, fixture

PNG = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=")

async def main():
    users, sessions, logins, cid = fixture()
    checks = []
    def passed(text): checks.append(text); print("PASS:", text, flush=True)
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(headless=True, args=["--no-sandbox"])
        # NOTE: service workers are NOT blocked here (unlike the other suites).
        context = await browser.new_context(viewport={"width": 1440, "height": 1000}, accept_downloads=True)
        page_errors = []

        async def new_chat_page(login, route):
            page = await context.new_page()
            await page.add_init_script("localStorage.setItem('authSession', " + json.dumps(json.dumps(login)) + "); localStorage.setItem('apiBaseUrl', location.origin);")
            page.on("pageerror", lambda e: page_errors.append(str(e)))
            await page.goto(BASE + route, wait_until="domcontentloaded")
            await expect(page.get_by_test_id("chat-connection")).to_have_attribute("data-connected", "true", timeout=60000)
            return page

        # --- load the app once so the service worker registers, then reload to become controlled ---
        sender = await new_chat_page(logins[0], f"/chat/{cid}")
        sw_active = await sender.evaluate("navigator.serviceWorker.ready.then(r => !!r.active)")
        assert sw_active, "service worker did not activate"
        await sender.reload(wait_until="domcontentloaded")
        await expect(sender.get_by_test_id("chat-connection")).to_have_attribute("data-connected", "true", timeout=60000)
        assert await sender.evaluate("() => !!navigator.serviceWorker.controller"), "page not controlled by SW"
        passed("Service worker is installed, active and controls the chat page")

        recipient = await new_chat_page(logins[1], "/chat")
        peer_item = recipient.locator(f'.dialog-item[data-conversation-id="{cid}"]')
        await expect(peer_item).to_be_visible(timeout=10000)
        await peer_item.click()
        assert len(context.pages) == 2

        async def upload_and_send(name, mime, content):
            await sender.bring_to_front()
            await sender.locator('input[type="file"]').set_input_files({"name": name, "mimeType": mime, "buffer": content})
            await expect(sender.get_by_test_id("attachment-ready")).to_contain_text(name, timeout=20000)
            async with sender.expect_response(lambda r: r.request.method == "POST" and r.url.split('?')[0] == BASE + f"/api/chat/conversations/{cid}/messages") as info:
                await sender.locator(".btn-send").click()
            response = await info.value
            assert response.ok, await response.text()
            message = await response.json()
            # Wait until Blazor finished rendering the new message (the file input is recreated),
            # otherwise the next selection can target the stale input element.
            await expect(sender.locator(f"#msg-{message['id']}.outgoing")).to_be_visible(timeout=10000)
            return message

        file_content = "محتوا برای تست سرویس‌ورکر\n123456".encode()
        file_msg = await upload_and_send("pwa-file.txt", "text/plain", file_content)
        image_msg = await upload_and_send("pwa-img.png", "image/png", PNG)
        passed("Two attachment messages (text + image) are sent and rendered")

        # --- 1) download button: same page, no extra tab ---
        await recipient.bring_to_front()
        row = recipient.locator(f"#msg-{file_msg['id']}")
        await expect(row).to_contain_text("pwa-file.txt")
        pages_before = len(context.pages)
        async with recipient.expect_download(timeout=30000) as dl_info:
            await row.locator(".file-dl-btn").click()
        download = await dl_info.value
        assert download.suggested_filename == "pwa-file.txt"
        assert Path(await download.path()).read_bytes() == file_content
        assert len(context.pages) == pages_before, "download opened an extra tab/page"
        passed("File download starts in the SAME page with the SW active (no new tab), exact name and bytes")

        # --- 2) image click opens the in-page lightbox; its download works in place ---
        img_row = recipient.locator(f"#msg-{image_msg['id']}")
        await expect(img_row.locator("img.bubble-img")).to_be_visible(timeout=15000)
        await img_row.locator("img.bubble-img").click()
        lightbox = recipient.get_by_test_id("chat-image-lightbox")
        await expect(lightbox).to_be_visible(timeout=10000)
        assert len(context.pages) == 2, "image click opened an extra tab instead of the lightbox"
        img = lightbox.locator("img[data-testid='lightbox-image']")
        natural = await img.evaluate("el => el.complete && el.naturalWidth > 0 ? el.naturalWidth : 0")
        assert natural > 0, "lightbox image did not load"
        passed("Clicking the image opens the in-page lightbox (no new tab) and the image loads from the API")

        pages_before = len(context.pages)
        async with recipient.expect_download(timeout=30000) as dl_info:
            await lightbox.locator("a.chat-lightbox-dl").click()
        img_download = await dl_info.value
        assert img_download.suggested_filename == "pwa-img.png"
        assert Path(await img_download.path()).read_bytes() == PNG
        assert len(context.pages) == pages_before
        await lightbox.get_by_test_id("close-lightbox").click()
        await expect(lightbox).to_have_count(0)
        passed("Lightbox download works in place (no new tab) and the lightbox closes")

        # --- 3) controlled page fetches/navigations to data URLs must reach the server ---
        dl_url = BASE + f"/api/chat/messages/{file_msg['id']}/download?access_token={logins[1]['token']}"
        prev_url = BASE + f"/api/chat/messages/{image_msg['id']}/preview?access_token={logins[1]['token']}"
        via_fetch = await recipient.evaluate("""async (url) => {
            const r = await fetch(url, { credentials: 'omit' });
            const b = await r.arrayBuffer();
            return { status: r.status, ct: r.headers.get('content-type'),
                     cd: r.headers.get('content-disposition'), len: b.byteLength };
        }""", dl_url)
        assert via_fetch["status"] == 200 and via_fetch["ct"] and "text/html" not in via_fetch["ct"] \
            and "attachment" in (via_fetch["cd"] or "") and via_fetch["len"] == len(file_content), via_fetch
        via_fetch_img = await recipient.evaluate("""async (url) => {
            const r = await fetch(url, { credentials: 'omit' });
            return { status: r.status, ct: r.headers.get('content-type'), len: (await r.arrayBuffer()).byteLength };
        }""", prev_url)
        assert via_fetch_img["status"] == 200 and via_fetch_img["ct"] == "image/png" \
            and via_fetch_img["len"] == len(PNG), via_fetch_img
        passed("SW lets download/preview API requests reach the server (no app-shell, no token caching)")

        # direct navigation in a brand-new controlled tab to the download URL: real download, not the app
        p3 = await context.new_page()
        await p3.goto(BASE + "/chat", wait_until="domcontentloaded")
        await p3.reload(wait_until="domcontentloaded")  # second navigation -> controlled by the SW
        assert await p3.evaluate("() => !!navigator.serviceWorker.controller")
        async with p3.expect_download(timeout=30000) as dl_info:
            try:
                await p3.goto(dl_url, wait_until="domcontentloaded")
            except Exception:
                pass  # navigation is aborted once the download starts
        direct = await dl_info.value
        assert Path(await direct.path()).read_bytes() == file_content
        await p3.close()
        # direct navigation to the preview URL renders the image inline, not the app shell
        p4 = await context.new_page()
        await p4.goto(BASE + "/chat", wait_until="domcontentloaded")
        await p4.reload(wait_until="domcontentloaded")
        resp = await p4.goto(prev_url, wait_until="domcontentloaded")
        assert resp is not None and resp.headers.get("content-type", "").startswith("image/")
        await p4.close()
        passed("Opening a file URL directly in a new tab downloads/renders the file (never the app shell)")

        # --- 4) ordinary non-API navigation still gets the cached app shell (guard is targeted) ---
        p5 = await context.new_page()
        await p5.goto(BASE + "/chat", wait_until="domcontentloaded")
        await p5.reload(wait_until="domcontentloaded")
        assert await p5.evaluate("() => !!navigator.serviceWorker.controller")
        resp = await p5.goto(BASE + "/totally-not-an-api-route", wait_until="domcontentloaded")
        assert resp is not None and "text/html" in resp.headers.get("content-type", "")
        body = (await p5.content()).lower()
        assert "<!doctype html>" in body, "expected the cached app shell for an ordinary route"
        await p5.close()
        passed("Ordinary SPA navigations still get the cached app shell (guard is scoped to data URLs)")

        assert not page_errors, page_errors
        passed("No uncaught page errors in the SW-enabled chat session")

        await browser.close()
    print(f"All {len(checks)} PWA download scenarios passed.")

asyncio.run(main())
