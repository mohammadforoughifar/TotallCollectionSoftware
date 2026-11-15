#!/usr/bin/env python3
"""تست سرویس OCR در حال اجرا (نیازمند: سرویس روشن + نمونه در tools/samples).

اجرا:  python test_service.py [BASE_URL]
پیش‌نیاز ساخت نمونه:  python tools/make_samples.py
"""
from __future__ import annotations

import os
import sys
import urllib.request

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:8765"
HERE = os.path.dirname(os.path.abspath(__file__))
S = os.path.join(HERE, "tools", "samples")

failures: list[str] = []


def post(path: str, filename: str, extra: list[tuple[str, str]] | None = None) -> dict:
    boundary = "----totallocr" + os.urandom(8).hex()
    parts = []
    for k, v in (extra or []):
        parts.append(
            f"--{boundary}\r\nContent-Disposition: form-data; name=\"{k}\"\r\n\r\n{v}\r\n".encode()
        )
    with open(os.path.join(S, filename), "rb") as f:
        data = f.read()
    parts.append(
        f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{filename}\"\r\n"
        f"Content-Type: application/octet-stream\r\n\r\n".encode() + data + b"\r\n"
    )
    parts.append(f"--{boundary}--\r\n".encode())
    body = b"".join(parts)
    req = urllib.request.Request(BASE + path, data=body, method="POST")
    req.add_header("Content-Type", f"multipart/form-data; boundary={boundary}")
    with urllib.request.urlopen(req, timeout=600) as resp:
        import json

        return json.loads(resp.read().decode())


def check(name: str, ok: bool, detail: str = "") -> None:
    status = "PASS" if ok else "FAIL"
    print(f"{status}: {name}" + (f" — {detail}" if detail else ""))
    if not ok:
        failures.append(name)


def main() -> None:
    with urllib.request.urlopen(BASE + "/health", timeout=30) as r:
        import json

        health = json.loads(r.read().decode())
    check("health", health.get("status") == "ok", f"status={health.get('status')} engine={health.get('engine')}")
    if health.get("status") != "ok":
        print("سرویس موتور آماده ندارد؛ خروج.", file=sys.stderr)
        sys.exit(1)

    r = post("/api/ocr/image", "persian_sample.png")
    check("OCR تصویر فارسی موفق", r.get("success") is True, f"engine={r.get('engine')}")
    text = r.get("text") or ""
    for token in ["قرارداد", "شرکت", "۱۴۰۵", "تهران", "ریال"]:
        check(f"واژهٔ «{token}» در خروجی تصویر", token in text, text[:90].replace("\n", " | "))
    check("شمارهٔ نویسه‌ها", (r.get("characterCount") or 0) > 30, f"{r.get('characterCount')} نویسه")

    r = post("/api/ocr/pdf", "scanned_persian.pdf")
    check("OCR PDF اسکن‌شده (فارسی)", r.get("success") is True, f"method={r.get('method')} engine={r.get('engine')}")
    ptext = r.get("text") or ""
    check("واژهٔ «قرارداد» در PDF", "قرارداد" in ptext, ptext[:90].replace("\n", " | "))
    check("PDF به‌صورت pdf-ocr شناسایی شد", r.get("method") == "pdf-ocr", f"ocrPages={r.get('ocrPages')}")

    r = post("/api/ocr/pdf", "digital_en.pdf")
    check("PDF دیجیتال (لایهٔ متن)", r.get("success") is True and r.get("method") == "pdf-text",
          f"method={r.get('method')} text={ (r.get('text') or '')[:60]!r}")

    if failures:
        print(f"\n{len(failures)} تست ناموفق: {failures}")
        sys.exit(1)
    print("\nهمهٔ تست‌های سرویس OCR موفق بودند ✔")


if __name__ == "__main__":
    main()
