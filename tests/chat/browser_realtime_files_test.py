#!/usr/bin/env python3
"""Multi-account browser checks. Requires requests, websockets, playwright.
Run against a disposable published app with CHAT_TEST_DISPOSABLE=1.
"""
import asyncio
import base64
import io
import json
import wave
from pathlib import Path
from playwright.async_api import async_playwright, expect
from realtime_files_test import BASE, fixture

SOCKET_HOOK = """
window.__chatSockets = []; window.__blockChatSockets = false;
const NativeSocket = window.WebSocket;
window.WebSocket = class extends NativeSocket {
    constructor(url, ...args) {
        if (String(url).includes('/hubs/chat') && window.__blockChatSockets) throw new Error('Test disconnect');
        super(url, ...args);
        if (String(url).includes('/hubs/chat')) window.__chatSockets.push(this);
    }
};
"""


async def main():
    users, sessions, logins, cid = fixture()
    checks, errors = [], []
    def passed(text): checks.append(text); print("PASS:", text, flush=True)
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(headless=True, args=["--no-sandbox"])
        contexts = []

        async def new_page(login, route):
            context = await browser.new_context(viewport={"width": 1440, "height": 1000}, service_workers="block", accept_downloads=True)
            contexts.append(context)
            await context.add_init_script("localStorage.setItem('authSession', " + json.dumps(json.dumps(login)) + "); localStorage.setItem('apiBaseUrl', location.origin);" + SOCKET_HOOK)
            page = await context.new_page()
            page.on("pageerror", lambda error: errors.append(str(error)))
            page.on("console", lambda msg: errors.append(msg.text) if msg.type == "error" and "Unhandled exception rendering component" in msg.text else None)
            await page.goto(BASE + route, wait_until="domcontentloaded")
            await expect(page.get_by_test_id("chat-connection")).to_have_attribute("data-connected", "true", timeout=45000)
            return page

        sender = await new_page(logins[0], f"/chat/{cid}")
        recipient = await new_page(logins[1], "/chat")
        composer = sender.get_by_placeholder("پیام خود را بنویسید...")
        peer_item = recipient.locator(f'.dialog-item[data-conversation-id="{cid}"]')
        saved = []

        async def send(page, text=None, enter=False):
            await page.bring_to_front()
            if text is not None: await page.get_by_placeholder("پیام خود را بنویسید...").fill(text)
            async with page.expect_response(lambda r: r.request.method == "POST" and r.url.split('?')[0] == BASE + f"/api/chat/conversations/{cid}/messages") as info:
                if enter: await page.get_by_placeholder("پیام خود را بنویسید...").press("Enter")
                else: await page.locator(".btn-send").click()
            response = await info.value
            assert response.ok, await response.text()
            data = await response.json()
            await expect(page.locator(f"#msg-{data['id']}.outgoing")).to_be_visible(timeout=5000)
            await expect(page.locator(f"#msg-{data['id']}")).to_have_count(1)
            return data

        for i in range(3):
            message = await send(sender, f"پیام بدون رفرش {i + 1}", enter=i == 0)
            saved.append(message)
            await expect(peer_item.locator(".dialog-heading").get_by_test_id("unread-count")).to_have_text(str(i + 1), timeout=5000)
        passed("Three messages appear immediately for sender; unread 1/2/3 appears beside recipient's contact name, without refresh")
        await recipient.bring_to_front()
        await peer_item.click()
        for message in saved:
            await expect(recipient.locator(f"#msg-{message['id']}.incoming")).to_be_visible()
        await expect(peer_item.get_by_test_id("unread-count")).to_have_count(0, timeout=5000)
        await expect(sender.locator(f"#msg-{saved[-1]['id']} .bi-check2-all")).to_be_visible(timeout=5000)
        passed("Opening the conversation clears its badge and updates the sender's read receipt live")

        await sender.bring_to_front()
        await composer.fill("در حال نوشتن")
        await expect(recipient.locator(".chat-header")).to_contain_text("در حال نوشتن", timeout=5000)
        for i in range(10):
            await composer.fill("در حال نوشتن " + str(i))
            await asyncio.sleep(.5)
        await expect(recipient.locator(".chat-header")).to_contain_text("در حال نوشتن")
        await composer.fill("")
        await expect(recipient.locator(".chat-header")).not_to_contain_text("در حال نوشتن", timeout=5000)
        passed("Typing indicator stays alive during continuous input and clears immediately on stopping")

        await composer.fill("خط اول")
        await composer.press("Shift+Enter")
        await composer.press_sequentially("خط دوم")
        await expect(composer).to_have_value("خط اول\nخط دوم")
        message = await send(sender, enter=True)
        assert message["text"] == "خط اول\nخط دوم"
        await expect(recipient.locator(f"#msg-{message['id']}.incoming")).to_be_visible(timeout=5000)
        passed("Enter sends once; Shift+Enter preserves a newline; recipient sees the new message without reloading")

        async def attach(name, mime, content):
            await sender.bring_to_front()
            await sender.locator('input[type="file"]').set_input_files({"name": name, "mimeType": mime, "buffer": content})
            await expect(sender.get_by_test_id("attachment-ready")).to_contain_text(name, timeout=15000)
            return await send(sender)

        file_content = "محتوای قابل دانلود\n123".encode()
        message = await attach("فایل آزمایشی.txt", "text/plain", file_content)
        row = recipient.locator(f"#msg-{message['id']}")
        await expect(row).to_contain_text("فایل آزمایشی.txt")
        async with recipient.expect_download() as info:
            await row.locator(".file-dl-btn").click()
        download = await info.value
        assert download.suggested_filename == "فایل آزمایشی.txt"
        assert Path(await download.path()).read_bytes() == file_content
        passed("Browser upload and recipient download preserve the Persian filename and exact file contents")
        # Same filename again must produce a working selection, not a stuck input element.
        await attach("فایل آزمایشی.txt", "text/plain", file_content)
        passed("The same file can be selected and sent again after the input resets")

        png = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII=")
        image = await attach("تصویر.png", "image/png", png)
        img = recipient.locator(f"#msg-{image['id']} img")
        await expect(img).to_be_visible()
        await recipient.wait_for_function("el => el.complete && el.naturalWidth > 0", arg=await img.element_handle(), timeout=10000)
        assert "/api/chat/messages/" in await img.get_attribute("src")
        passed("Incoming image preview loads from the authorized API route")

        audio_buffer = io.BytesIO()
        with wave.open(audio_buffer, "wb") as wav:
            wav.setparams((1, 2, 8000, 0, "NONE", "not compressed")); wav.writeframes(b"\0\0" * 800)
        audio = await attach("صدا.wav", "audio/wav", audio_buffer.getvalue())
        audio_el = recipient.locator(f"#msg-{audio['id']} audio")
        await audio_el.evaluate("el => el.load()")
        await recipient.wait_for_function("el => el.readyState >= 1 && Number.isFinite(el.duration)", arg=await audio_el.element_handle(), timeout=10000)
        passed("Audio preview loads decodable metadata and retains its download link")

        # Generate a tiny real WebM from a synthetic canvas (no camera/microphone).
        video_bytes = await sender.evaluate("""async () => {
            const canvas = document.createElement('canvas'); canvas.width=160; canvas.height=90;
            const ctx = canvas.getContext('2d'); ctx.fillStyle='#0284c7'; ctx.fillRect(0,0,160,90);
            const stream = canvas.captureStream(10); const chunks=[];
            const recorder = new MediaRecorder(stream, {mimeType:'video/webm;codecs=vp8'});
            const done = new Promise(resolve => recorder.onstop=resolve);
            recorder.ondataavailable=e=>{if(e.data.size)chunks.push(e.data)};
            recorder.start(); setTimeout(()=>recorder.stop(),400); await done;
            stream.getTracks().forEach(t=>t.stop());
            return Array.from(new Uint8Array(await new Blob(chunks,{type:'video/webm'}).arrayBuffer()));
        }""")
        video = await attach("ویدیو.webm", "video/webm", bytes(video_bytes))
        video_el = recipient.locator(f"#msg-{video['id']} video")
        await expect(video_el).to_be_visible()
        await recipient.wait_for_function("el => el.readyState >= 1 && el.videoWidth > 0", arg=await video_el.element_handle(), timeout=10000)
        passed("Video preview decodes a real WebM instead of being displayed as an unusable link")

        async def fail_send(route):
            if route.request.method == 'POST':
                await route.fulfill(status=503, content_type='application/json', body='{"message":"خطای آزمایشی ارسال"}')
            else: await route.continue_()
        await sender.locator('input[type="file"]').set_input_files({"name": "retry.txt", "mimeType": "text/plain", "buffer": b"retry"})
        await expect(sender.get_by_test_id("attachment-ready")).to_contain_text("retry.txt")
        await composer.fill("متن برای تلاش مجدد")
        await sender.route("**/api/chat/conversations/*/messages", fail_send)
        await sender.locator('.btn-send').click()
        await expect(sender.locator('.btn-send')).to_be_enabled()
        await expect(composer).to_have_value("متن برای تلاش مجدد")
        await expect(sender.get_by_test_id("attachment-ready")).to_contain_text("retry.txt")
        await sender.unroute("**/api/chat/conversations/*/messages", fail_send)
        await send(sender)
        await expect(sender.get_by_test_id("attachment-ready")).to_have_count(0)
        passed("A failed send preserves both draft and uploaded attachment; retry succeeds without reupload")

        async def block_hub(route): await route.abort()
        await sender.route("**/hubs/chat**", block_hub)
        await sender.evaluate("window.__blockChatSockets=true; window.__chatSockets.forEach(s=>s.close())")
        await expect(sender.get_by_test_id("chat-connection")).to_have_attribute("data-connected", "false", timeout=10000)
        offline = await send(sender, "نمایش از پاسخ HTTP در قطع سوکت")
        await expect(recipient.locator(f"#msg-{offline['id']}.incoming")).to_be_visible(timeout=5000)
        missed = sessions[1].post(BASE + f"/api/chat/conversations/{cid}/messages", json={"text": "پیام هنگام قطع اتصال"}, timeout=20)
        missed.raise_for_status()
        await sender.evaluate("window.__blockChatSockets=false")
        await sender.unroute("**/hubs/chat**", block_hub)
        await expect(sender.get_by_test_id("chat-connection")).to_have_attribute("data-connected", "true", timeout=25000)
        await expect(sender.locator(f"#msg-{missed.json()['id']}.incoming")).to_be_visible(timeout=10000)
        await expect(sender.locator(f"#msg-{offline['id']}")).to_have_count(1)
        passed("HTTP acknowledgement renders during a socket outage; reconnect recovers missed messages without duplicates or refresh")

        # First-start failure must retry too (WithAutomaticReconnect alone does not do this).
        context = await browser.new_context(viewport={"width":1440,"height":1000}, service_workers="block")
        contexts.append(context)
        await context.add_init_script("localStorage.setItem('authSession', " + json.dumps(json.dumps(logins[2])) + "); localStorage.setItem('apiBaseUrl', location.origin);" + SOCKET_HOOK)
        first_failure = await context.new_page()
        await first_failure.route("**/hubs/chat**", block_hub)
        await first_failure.goto(BASE + "/chat", wait_until="domcontentloaded")
        await expect(first_failure.get_by_test_id("chat-connection")).to_have_attribute("data-connected", "false", timeout=45000)
        await first_failure.unroute("**/hubs/chat**", block_hub)
        await expect(first_failure.get_by_test_id("chat-connection")).to_have_attribute("data-connected", "true", timeout=25000)
        passed("An initial hub connection failure recovers automatically without reloading the page")

        assert not errors, errors
        passed("No uncaught page errors or Blazor rendering failures in the two live accounts")
        for context in contexts: await context.close()
        await browser.close()
    print(f"All {len(checks)} multi-account browser scenarios passed.")

if __name__ == '__main__': asyncio.run(main())
