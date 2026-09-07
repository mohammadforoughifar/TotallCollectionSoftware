#!/usr/bin/env python3
"""Browser regression tests against a disposable, published, single-server app.

Requires: pip install requests playwright && playwright install chromium
Run only with CHAT_TEST_DISPOSABLE=1; this creates test users and conversations.
CHAT_TEST_BASE_URL defaults to http://127.0.0.1:5100.
CHAT_TEST_USERNAME / CHAT_TEST_PASSWORD default to the local demo admin account.
"""
import asyncio
import json
import os
import uuid
from urllib.parse import parse_qs, urlparse

import requests
from playwright.async_api import async_playwright, expect

BASE = os.environ.get("CHAT_TEST_BASE_URL", "http://127.0.0.1:5100").rstrip("/")


def fixtures():
    if os.environ.get("CHAT_TEST_DISPOSABLE") != "1":
        raise SystemExit("Use a disposable test database and set CHAT_TEST_DISPOSABLE=1.")
    admin = requests.Session()
    login = admin.post(BASE + "/api/auth/login", json={
        "username": os.environ.get("CHAT_TEST_USERNAME", "admin"),
        "password": os.environ.get("CHAT_TEST_PASSWORD", "admin")}, timeout=20)
    login.raise_for_status()
    admin.headers["Authorization"] = "Bearer " + login.json()["token"]
    suffix = uuid.uuid4().hex[:8]
    password = "LocalChatTest123!"
    users = {}
    for key, first, last, active in [
        ("viewer", "آزمایش", suffix, True),
        ("ali", "علي", "كريمي " + suffix, True),
        ("sara", "سارا", "احمدی " + suffix, True),
        ("inactive", "علي", "كريمي " + suffix, False),
    ]:
        response = admin.post(BASE + "/api/users", json={
            "username": f"search_{key}_{suffix}", "password": password,
            "firstName": first, "lastName": last, "isActive": active,
            "role": "Operator"}, timeout=20)
        response.raise_for_status()
        users[key] = response.json()
    login = requests.post(BASE + "/api/auth/login", json={
        "username": users["viewer"]["username"], "password": password}, timeout=20)
    login.raise_for_status()
    session = login.json()
    assert any(p.startswith("Chat.") for p in session["permissions"])
    viewer = requests.Session()
    viewer.headers["Authorization"] = "Bearer " + session["token"]
    assert viewer.get(BASE + "/api/chat/conversations", timeout=20).json() == []
    return session, viewer, users, suffix


async def main():
    session, viewer, users, suffix = fixtures()
    target = users["ali"]["username"]
    other = users["sara"]["username"]
    full_name = "علی کریمی " + suffix
    checks = []

    def passed(message):
        checks.append(message)
        print("PASS:", message, flush=True)

    async with async_playwright() as pw:
        browser = await pw.chromium.launch(headless=True, args=["--no-sandbox"])
        context = await browser.new_context(viewport={"width": 1440, "height": 1000}, service_workers="block")
        await context.add_init_script("localStorage.setItem('authSession', " +
            json.dumps(json.dumps(session)) + "); localStorage.setItem('apiBaseUrl', location.origin);")
        page = await context.new_page()
        errors = []
        page.on("pageerror", lambda error: errors.append(str(error)))
        await page.goto(BASE + "/chat", wait_until="domcontentloaded")
        search = page.get_by_role("textbox", name="جستجوی کاربران و گفتگوها", exact=True)
        await expect(search).to_be_visible(timeout=45000)
        contacts = page.get_by_test_id("chat-contact-results")
        picker = page.get_by_test_id("chat-user-picker")

        async def assert_target(container):
            await expect(container.locator(".user-picker-item").filter(has_text=target)).to_be_visible(timeout=15000)
            await expect(container.locator(".user-picker-item")).to_have_count(1)

        await search.fill(full_name)
        await assert_target(contacts)
        passed("Sidebar finds an offline colleague with no previous conversation using a Persian full name")
        await contacts.locator(".user-picker-item").filter(has_text=target).click()
        await expect(page.locator(".chat-header")).to_contain_text("كريمي " + suffix)
        await expect(search).to_have_value("")
        conversations = viewer.get(BASE + "/api/chat/conversations", timeout=20).json()
        assert len(conversations) == 1 and conversations[0]["directPeerUserId"] == users["ali"]["id"]
        passed("Selecting a search result starts a direct chat with the canonical login Id")

        await page.get_by_title("گفتگوی دونفره جدید", exact=True).click()
        user_search = page.get_by_role("textbox", name="جستجوی کاربران", exact=True)
        await user_search.fill("  علی‌کریمی   " + suffix)
        await assert_target(picker)
        passed("New-chat picker searches full names with Persian/Arabic variants, spaces and ZWNJ")
        await user_search.fill("no_matching_person_" + suffix)
        await expect(picker).to_contain_text("کاربری با این مشخصات یافت نشد.")
        await expect(picker.locator(".user-picker-item")).to_have_count(0)
        passed("A missing user shows a true empty result, not the whole staff list")

        async def fail_users(route):
            await route.fulfill(status=503, content_type="application/json",
                body=json.dumps({"message": "خطای آزمایشی ارتباط"}))

        await page.route("**/api/chat/users*", fail_users)
        await user_search.fill(target)
        await expect(picker.get_by_role("alert")).to_contain_text("دریافت کاربران ناموفق بود")
        await expect(picker.locator(".user-picker-item")).to_have_count(0)
        await page.unroute("**/api/chat/users*", fail_users)
        await picker.get_by_role("button", name="تلاش مجدد", exact=True).click()
        await assert_target(picker)
        passed("Picker distinguishes server failure from no match and retry recovers")

        async def stale_response_test(input_box, container):
            started = asyncio.Event()

            async def delay_old(route):
                query = parse_qs(urlparse(route.request.url).query).get("search", [""])[0]
                if query == other:
                    started.set()
                    await asyncio.sleep(1.2)
                await route.continue_()

            await page.route("**/api/chat/users*", delay_old)
            async with page.expect_response(lambda response:
                    urlparse(response.url).path == "/api/chat/users" and
                    parse_qs(urlparse(response.url).query).get("search") == [other]) as delayed:
                await input_box.fill(other)
                await asyncio.wait_for(started.wait(), timeout=10)
                await input_box.fill(target)
                await assert_target(container)
            response = await delayed.value
            await response.body()
            # Give Blazor time to consume the deliberately late HTTP body.
            await page.wait_for_timeout(250)
            await assert_target(container)
            await page.unroute("**/api/chat/users*", delay_old)

        await stale_response_test(user_search, picker)
        passed("A late picker response cannot overwrite the latest query")
        await page.locator(".modal").get_by_role("button", name="بستن", exact=True).click()
        await page.get_by_title("گفتگوی دونفره جدید", exact=True).click()
        await expect(user_search).to_have_value("")
        await expect(picker.locator(".user-picker-item").filter(has_text=target)).to_be_visible()
        await expect(picker.locator(".user-picker-item").filter(has_text=users["viewer"]["username"])).to_have_count(0)
        await expect(picker.locator(".user-picker-item").filter(has_text=users["inactive"]["username"])).to_have_count(0)
        passed("Reopening resets search; self and inactive logins are excluded")
        await page.locator(".modal").get_by_role("button", name="بستن", exact=True).click()

        await search.fill(target)
        await assert_target(contacts)
        await stale_response_test(search, contacts)
        passed("A late sidebar response cannot overwrite the latest query")
        await page.route("**/api/chat/users*", fail_users)
        await search.fill(other)
        await expect(contacts.get_by_role("alert")).to_contain_text("دریافت کاربران ناموفق بود")
        await page.unroute("**/api/chat/users*", fail_users)
        await contacts.get_by_role("button", name="تلاش مجدد", exact=True).click()
        await expect(contacts.locator(".user-picker-item").filter(has_text=other)).to_be_visible()
        passed("Sidebar also displays a real API error and offers a working retry")
        await page.locator(".btn-clear-search").click()
        await expect(contacts).to_have_count(0)

        await page.get_by_title("ایجاد گروه کاری", exact=True).click()
        await user_search.fill(target)
        await assert_target(picker)
        await picker.locator(".user-picker-item").filter(has_text=target).click()
        await user_search.fill(other)
        await expect(picker.locator(".user-picker-item").filter(has_text=other)).to_be_visible()
        await picker.locator(".user-picker-item").filter(has_text=other).click()
        await expect(page.locator(".modal")).to_contain_text("2 نفر انتخاب شده")
        group_title = "گروه تست جستجو " + suffix
        title_input = page.get_by_placeholder("مثال: کارگروه پروژه پالایشگاه")
        await title_input.fill(group_title)
        await title_input.press("Tab")  # The existing title field commits on change/blur.
        await page.get_by_role("button", name="ساخت گروه", exact=True).click()
        await expect(page.locator(".chat-header")).to_contain_text(group_title)
        conversations = viewer.get(BASE + "/api/chat/conversations", timeout=20).json()
        group = next(c for c in conversations if c["title"] == group_title)
        assert group["membersCount"] == 3
        passed("Group search keeps selections across queries and creates the correct three-member group")

        assert not errors, errors
        passed("No uncaught browser page errors")
        await browser.close()
    print(f"All {len(checks)} browser scenarios passed.", flush=True)


if __name__ == "__main__":
    asyncio.run(main())
