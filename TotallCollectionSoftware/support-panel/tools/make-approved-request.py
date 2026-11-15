#!/usr/bin/env python3
"""ساخت یک پروندهٔ «تأییدشده» (Approved) برای عکس‌برداری راهنما.

مراحل: ورود دمو → ثبت درخواست از JSON → وصل‌کردن مشتری → شروع بررسی →
ثبت تأیید هویت → تأیید درخواست.  خروجی: شناسهٔ پرونده روی stdout.

استفاده:  python3 tools/make-approved-request.py [--customer 2]
"""
import argparse
import html
import http.client
import json
import re
import time
import urllib.parse

BASE_HOST, BASE_PORT = "127.0.0.1", 5399


class Client:
    def __init__(self) -> None:
        self.cookies: dict[str, str] = {}

    def req(self, method: str, path: str, body: bytes | None = None):
        conn = http.client.HTTPConnection(BASE_HOST, BASE_PORT, timeout=30)
        headers = {"Host": f"{BASE_HOST}:{BASE_PORT}", "X-Forwarded-Proto": "https"}
        if self.cookies:
            headers["Cookie"] = "; ".join(f"{k}={v}" for k, v in self.cookies.items())
        if body is not None:
            headers["Content-Type"] = "application/x-www-form-urlencoded"
            headers["Content-Length"] = str(len(body))
        conn.request(method, path, body=body, headers=headers)
        resp = conn.getresponse()
        text = resp.read().decode("utf-8", "replace")
        for key, value in resp.getheaders():
            if key.lower() == "set-cookie":
                pair = value.split(";")[0]
                if "=" in pair:
                    name, val = pair.split("=", 1)
                    self.cookies[name.strip()] = val.strip()
        conn.close()
        return resp.status, resp.getheader("Location"), text

    def token(self, path: str) -> tuple[str, str]:
        text = self.req("GET", path)[2]
        match = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', text)
        return (match.group(1) if match else ""), text

    def post(self, path: str, fields: dict) -> tuple[int, str | None, str]:
        return self.req("POST", path, urllib.parse.urlencode(fields).encode())


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--customer", type=int, default=2)
    args = parser.parse_args()

    cli = Client()
    page = cli.req("GET", "/Login")[2]
    token = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', page).group(1)
    cli.post("/Login?handler=Demo", {"__RequestVerificationToken": token})

    inst = "guide-" + time.strftime("%H%M%S")
    payload = json.dumps({
        "requestVersion": 2, "requestType": "NewActivation", "installationId": inst,
        "customerName": "شرکت راهنمای تصویری", "contactName": "مدیر IT",
        "contactPhone": "02100000000", "customerNote": "نمونهٔ راهنمای تصویری",
        "requestedAt": "2026-11-15T00:00:00Z",
    }, ensure_ascii=False)
    token, _ = cli.token("/Requests/Ingest")
    _, location, _ = cli.post("/Requests/Ingest?handler=Save",
                              {"__RequestVerificationToken": token, "mode": "paste",
                               "rawChannel": "Paste", "pastedJson": payload})
    rid = re.search(r"id=(\d+)", location or "").group(1)

    token, _ = cli.token(f"/Requests/Details?id={rid}")
    cli.post(f"/Requests/Details?id={rid}&handler=LinkCustomer",
             {"__RequestVerificationToken": token, "id": rid, "customerId": str(args.customer)})

    token, _ = cli.token(f"/Requests/Details?id={rid}")
    cli.post(f"/Requests/Details?id={rid}&handler=Transition",
             {"__RequestVerificationToken": token, "id": rid, "to": "Reviewing", "note": "در حال بررسی"})

    token, page = cli.token(f"/Requests/Identity?id={rid}")
    # مقدار پیش‌فرض تاریخ در HTML به‌صورت entity (ارقام فارسی) می‌آید ⇒ باید رمزگشایی شود
    verified_at = html.unescape(re.search(r'name="verifiedAt"[^>]*value="([^"]*)"', page).group(1))
    cli.post(f"/Requests/Identity?id={rid}",
             {"__RequestVerificationToken": token, "id": rid, "method": "Phone",
              "verifiedWith": "مدیر IT", "verifiedAt": verified_at, "note": "تأیید تلفنی"})

    token, _ = cli.token(f"/Requests/Details?id={rid}")
    cli.post(f"/Requests/Details?id={rid}&handler=Transition",
             {"__RequestVerificationToken": token, "id": rid, "to": "Approved", "note": "تأیید درخواست"})

    print(rid)


if __name__ == "__main__":
    main()
