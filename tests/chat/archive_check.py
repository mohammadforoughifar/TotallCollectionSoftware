#!/usr/bin/env python3
"""Check the DocArchive screen presence on the published app: menu item, route, and role gating."""
import asyncio, json, os, sys
sys.path.insert(0, '.')
from playwright.async_api import async_playwright, expect
from realtime_files_test import BASE, fixture

async def probe(browser, login, role):
    context = await browser.new_context(viewport={"width": 1440, "height": 1000}, service_workers="block")
    await context.add_init_script("localStorage.setItem('authSession', " + json.dumps(json.dumps(login)) + "); localStorage.setItem('apiBaseUrl', location.origin);")
    page = await context.new_page()
    errors = []
    page.on("pageerror", lambda e: errors.append(str(e)))
    await page.goto(BASE + "/", wait_until="domcontentloaded")
    await page.wait_for_timeout(9000)  # let WASM boot + nav render
    # Sidebar/menu entry
    menu = page.locator("nav a, aside a, .nav-link, .sidebar a")
    menu_texts = await menu.all_inner_texts()
    has = any("آرشیو اسناد" in t for t in menu_texts)
    # Direct route
    await page.goto(BASE + "/doc-archive", wait_until="domcontentloaded")
    await page.wait_for_timeout(10000)
    body = await page.locator("body").inner_text()
    idx = body.find("آرشیو اسناد و مدارک")
    snippet = body[idx:idx + 420].replace("\n", " | ") if idx >= 0 else body[:200].replace("\n", " | ")
    summary = dict(role=role, menu_has_archive=has, route_snippet=snippet, pageerrors=errors)
    await context.close()
    return summary

async def main():
    users, sessions, logins, cid = fixture()
    async with async_playwright() as pw:
        browser = await pw.chromium.launch(headless=True, args=["--no-sandbox"])
        admin = requests_login("admin", "admin")
        for label, login in [("admin", admin), ("operator", logins[0])]:
            print(json.dumps(await probe(browser, login, label), ensure_ascii=False, indent=1))
        await browser.close()

import requests
def requests_login(u, p):
    r = requests.post(BASE + "/api/auth/login", json={"username": u, "password": p}, timeout=20)
    r.raise_for_status()
    return r.json()

asyncio.run(main())
