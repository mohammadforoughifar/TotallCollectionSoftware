#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ابزار «موقتِ» امضای لایسنس برای توسعه و تست — فقط تا آماده شدن LicenseTool (مرحله ۴).

⚠️ این فایل بخشی از محصول نیست و در بستهٔ نصب مشتری توزیع نمی‌شود.
   کلید خصوصی هیچ‌جا داخل مخزن نیست و همیشه با --private-key از بیرون داده می‌شود.

ساختار امضا دقیقاً همان چیزی است که Inventory.Licensing می‌فهمد:
  payload  = UTF-8 JSON سند (نسخه ۳) به‌صورت فشرده، سپس Base64
  signature= RSA-PKCS#1 v1.5 + SHA-256 روی همان بایت‌های payload، سپس Base64
            (معادل «openssl dgst -sha256 -sign key.pem»)

نمونهٔ استفاده:

  python tools/license-sign.py \
      --private-key ~/keys/licensing-dev-private.pem \
      --installation-id 3f2a...  \
      --customer-name "شرکت نمونه" \
      --module catalog:perpetual \
      --module letters:subscription:2027-11-08 \
      --users named:10 \
      --quota letter.create:2000:Monthly \
      --out license.lic

حالت «لایسنس پل» (برای مشتری موجودی که تازه نسخهٔ لایسنس‌دار را نصب می‌کند):

  python tools/license-sign.py --private-key ... --installation-id 3f2a... \
      --customer-name "شرکت نمونه" --bridge --days 30 --out bridge.lic

  لایسنس پل = یک Subscription کوتاه با **همهٔ** ماژول‌های فعلی و **بدون هیچ سقفی**.
  فرقش با مهلت داخلیِ نسخهٔ قبل: آن مهلت را خودِ نصب روی ماشین مشتری برای خودش
  تعیین می‌کرد و مشتری می‌توانست پاکش کند؛ این یکی **امضای ما** را دارد و در همان
  چارچوب سند لایسنس است. وقتی لایسنس اصلی صادر شد، فایل پل را می‌شود حذف کرد
  (ادغام اسناد: هر دو معتبر می‌مانند ولی سقف‌ها از آیتم دائمی می‌آیند).

قالب‌ها:
  --module  CODE:TERM[:UNTIL]
            TERM = perpetual | subscription | trial
            UNTIL = تاریخ پایان (YYYY-MM-DD)؛ برای اشتراکی/آزمایشی اجباری
  --users   named:N یا concurrent:N یا none
  --quota   METRIC:LIMIT:PERIOD[:PACKID]
            PERIOD = lifetime | monthly | yearly
            PACKID = فقط برای بستهٔ افزایشی (stackable)

شناسهٔ نصب (installation-id) را از صفحهٔ «فعال‌سازی» یا مسیر
C:\\ProgramData\\TotallCollection\\License\\installation.id بردارید.
"""

import argparse
import base64
import json
import subprocess
import sys
import tempfile
import uuid
from datetime import date, datetime, timedelta
from pathlib import Path

KEY_ID = "dev-2026a"          # همان keyId موجود در src/Inventory.Licensing/Keys/manifest.json
ALGORITHM = "RS256"


def iso_day(value: str | None) -> str | None:
    """تاریخ ISO کامل با ساعت صفر — چیزی که System.Text.Json در DateTime می‌پذیرد."""
    if not value:
        return None
    parsed = date.fromisoformat(value)
    return datetime(parsed.year, parsed.month, parsed.day).isoformat(timespec="seconds")


# ============================================================
#  کد ماژول‌ها — باید با src/Inventory.Licensing/Modules/ModuleCodes.cs یکی باشد.
#  (تست tests/Licensing این فهرست را با کد مقایسه می‌کند تا از هم جدا نیفتند.)
# ============================================================
ALL_MODULES = [
    "core", "dashboard", "cartable", "backup", "licensing",
    "catalog", "sales", "salesvisit", "warehousing", "stocktaking", "invoicing", "moadian",
    "accounting", "treasury", "expenses", "repairs", "projects", "workorders", "letters",
    "office_assets", "docarchive", "it_assets", "chat",
    "hr_fa", "hr_core", "faham_att", "faham_pay", "faham_lms", "faham_com", "bon_hr",
    "ai", "reports", "devteam",
]


def build_bridge_item(days: int, start: date | None = None) -> dict:
    """«لایسنس پل»: یک آیتم Subscription با همهٔ ماژول‌ها و بدون سقف.

    چرا **یک** آیتم و نه یکی برای هر ماژول؟
      • یک آیتم، یک `validUntil` دارد ⇒ یک تاریخ انقضا برای همه‌چیز، ساده و بی‌ابهام.
      • سقف‌ها خالی می‌ماند ⇒ «بدون سقف» (رفتار پیش‌فرض لایسنس برای متریک بی‌سهمیه).
      • ادغام با لایسنس اصلی: هر چه لایسنس اصلی بگوید (سقف/ماژول)، حاکم است چون
        سقف مؤثر = بیشترین سقف، و آیتم پل هیچ سقفی ندارد.
    """
    start = start or date.today()
    end = start + timedelta(days=int(days))

    return {
        "id": f"bridge-{uuid.uuid4().hex[:8]}",
        "scope": list(ALL_MODULES),
        "term": {
            "type": "Subscription",
            "validFrom": iso_day(start.isoformat()),
            "validUntil": iso_day(end.isoformat()),
        },
        "users": {"mode": "None", "max": 0, "appliesTo": "Global", "exempt": 0},
        "quotas": [],
        "note": f"لایسنس پل {days} روزه — همهٔ ماژول‌ها، بدون سقف",
    }


def build_item(spec: str) -> dict:
    """ساخت یک آیتم از رشتهٔ --module، به‌همراه کاربر و سهمیه‌هایش."""
    parts = spec.split(":")
    if len(parts) < 2:
        raise SystemExit(f"--module نامعتبر: {spec} (قالب: CODE:TERM[:UNTIL])")

    code, term_type = parts[0].strip(), parts[1].strip().lower()
    until = parts[2].strip() if len(parts) > 2 else None

    today = date.today()
    term: dict = {"type": term_type.capitalize()}

    if term_type == "perpetual":
        # آیتم دائمی: بدون تاریخ پایان؛ تاریخ شروع اختیاری
        term["validFrom"] = iso_day(until) if until else iso_day(today.isoformat())
    elif term_type in ("subscription", "trial"):
        if not until:
            until = (today + timedelta(days=365)).isoformat()
        term["validFrom"] = iso_day(today.isoformat())
        term["validUntil"] = iso_day(until)
    else:
        raise SystemExit(f"نوع مدل زمانی نامعتبر: {term_type}")

    return {
        "id": f"{code}-{uuid.uuid4().hex[:8]}",
        "scope": [code],
        "term": term,
        "users": {"mode": "None", "max": 0, "appliesTo": "Global", "exempt": 0},
        "quotas": [],
    }


def apply_users(items: list[dict], spec: str) -> None:
    """سیاست کاربر روی همهٔ آیتم‌ها اعمال می‌شود (در یک نصب فقط یک سیاست مجاز است)."""
    mode, _, max_seats = spec.partition(":")
    mode = mode.strip().lower()
    if mode == "none":
        for item in items:
            item["users"] = {"mode": "None", "max": 0, "appliesTo": "Global", "exempt": 0}
        return
    if mode not in ("named", "concurrent"):
        raise SystemExit(f"--users نامعتبر: {spec} (named:N یا concurrent:N یا none)")
    try:
        seats = int(max_seats)
    except ValueError:
        raise SystemExit(f"--users بدون سقف: {spec}")

    for item in items:
        item["users"] = {"mode": mode.capitalize(), "max": seats, "appliesTo": "Global", "exempt": 0}


def apply_quota(items: list[dict], spec: str) -> None:
    """سهمیه را به آیتمی که ماژولش با پیشوند متریک می‌خواند می‌چسباند."""
    parts = spec.split(":")
    if len(parts) < 3:
        raise SystemExit(f"--quota نامعتبر: {spec} (قالب: METRIC:LIMIT:PERIOD[:PACKID])")

    metric, limit_raw, period = parts[0].strip(), parts[1].strip(), parts[2].strip().lower()
    pack_id = parts[3].strip() if len(parts) > 3 else None

    try:
        limit = int(limit_raw)
    except ValueError:
        raise SystemExit(f"سقف سهمیه عدد نیست: {limit_raw}")

    period_map = {"lifetime": "Lifetime", "monthly": "Monthly", "yearly": "Yearly"}
    if period not in period_map:
        raise SystemExit(f"دورهٔ سهمیه نامعتبر: {period}")

    quota: dict = {"metric": metric, "limit": limit, "period": period_map[period]}
    if pack_id:
        quota["stackable"] = True
        quota["packId"] = pack_id

    # متریک به ماژولش تعلق دارد؛ آیتم همان ماژول را پیدا می‌کنیم.
    metric_module = {
        "invoice.create": "invoicing",
        "moadian.invoice.send": "moadian",
        "letter.create": "letters",
        "letter.inner.create": "letters",
        "letter.outgoing.create": "letters",
        "letter.incoming.create": "letters",
    }.get(metric)

    target = None
    if metric_module:
        target = next((i for i in items if metric_module in i["scope"]), None)
    if target is None:
        target = items[0] if items else None
    if target is None:
        raise SystemExit("برای سهمیه باید حداقل یک --module بدهید.")

    target["quotas"].append(quota)


def sign_payload(payload: bytes, private_key_path: Path) -> bytes:
    """امضا با openssl — بدون نیاز به کتابخانهٔ رمزنگاری پایتون."""
    with tempfile.TemporaryDirectory() as tmp:
        payload_path = Path(tmp) / "payload.json"
        signature_path = Path(tmp) / "signature.bin"
        payload_path.write_bytes(payload)

        result = subprocess.run(
            ["openssl", "dgst", "-sha256", "-sign", str(private_key_path),
             "-out", str(signature_path), str(payload_path)],
            capture_output=True, text=True)
        if result.returncode != 0:
            raise SystemExit(f"openssl شکست خورد: {result.stderr.strip()}")

        return signature_path.read_bytes()


def main() -> int:
    parser = argparse.ArgumentParser(description="امضای فایل لایسنس (ابزار توسعه)")
    parser.add_argument("--private-key", required=True, help="مسیر کلید خصوصی PEM (بیرون از مخزن)")
    parser.add_argument("--installation-id", required=True, help="شناسهٔ نصب (GUID) از صفحهٔ فعال‌سازی")
    parser.add_argument("--customer-id", default="CUST-DEV", help="کد مشتری")
    parser.add_argument("--customer-name", default="مشتری توسعه", help="نام مشتری")
    parser.add_argument("--serial", default=None, help="شماره سریال؛ پیش‌فرض: تصادفی")
    parser.add_argument("--module", action="append", default=[], help="CODE:TERM[:UNTIL] — تکرارشدنی")
    parser.add_argument("--users", default=None, help="named:N | concurrent:N | none")
    parser.add_argument("--quota", action="append", default=[], help="METRIC:LIMIT:PERIOD[:PACKID]")
    parser.add_argument("--out", required=True, help="مسیر فایل لایسنس خروجی")
    parser.add_argument("--bridge", action="store_true",
                        help="ساخت «لایسنس پل»: Subscription کوتاه با همهٔ ماژول‌ها و بدون سقف")
    parser.add_argument("--days", type=int, default=30,
                        help="طول لایسنس پل به روز (پیش‌فرض ۳۰؛ حداکثر ۹۰)")
    args = parser.parse_args()

    if args.bridge:
        # ---- حالت پل ----
        if args.module or args.quota:
            raise SystemExit("در حالت --bridge، --module و --quota معنا ندارد: پل همهٔ ماژول‌ها را "
                             "بدون سقف می‌دهد. برای لایسنس عادی، --bridge را برندارید.")

        days = max(1, min(int(args.days), 90))
        if days != args.days:
            print(f"⚠️  --days به {days} محدود شد (بازهٔ مجاز ۱..۹۰).")

        items = [build_bridge_item(days)]
        target_document_note = "لایسنس پل"
    else:
        if not args.module:
            raise SystemExit("حداقل یک --module لازم است (یا از --bridge استفاده کنید).")

        items = [build_item(m) for m in args.module]
        target_document_note = None

    if not args.bridge:
        if args.users:
            apply_users(items, args.users)
        for quota_spec in args.quota:
            apply_quota(items, quota_spec)

    document = {
        "version": 3,
        "keyId": KEY_ID,
        "serial": args.serial or f"DEV-{uuid.uuid4().hex[:12].upper()}",
        "customerId": args.customer_id,
        "customerName": args.customer_name,
        "deployment": "OnPrem",
        "installationId": args.installation_id,
        "issuedAt": datetime.utcnow().isoformat(timespec="seconds"),
        "items": items,
    }

    payload = json.dumps(document, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    signature = sign_payload(payload, Path(args.private_key).expanduser())

    envelope = {
        "version": 1,
        "keyId": KEY_ID,
        "algorithm": ALGORITHM,
        "payload": base64.b64encode(payload).decode("ascii"),
        "signature": base64.b64encode(signature).decode("ascii"),
    }

    out = Path(args.out).expanduser()
    out.write_text(json.dumps(envelope, ensure_ascii=False, indent=2), encoding="utf-8")

    print(f"فایل لایسنس ساخته شد: {out}")
    print(f"  سریال: {document['serial']}")
    print(f"  مشتری: {document['customerName']}")
    print(f"  آیتم‌ها: {len(items)}")
    print(f"  ماژول‌ها: {', '.join(m for i in items for m in i['scope'])}")

    if args.bridge:
        print(f"  نوع: لایسنس پل — اعتبار تا {items[0]['term']['validUntil'][:10]}")
        print("  ⚠️  این فایل یک راه‌حل موقت برای شروع کار است؛ لایسنس اصلی را جدا صادر کنید.")

    return 0


if __name__ == "__main__":
    sys.exit(main())
