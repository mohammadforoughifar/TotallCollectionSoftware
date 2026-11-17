#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
جایگزینی کلید عمومی رسمی محصول در Keys/ (فقط کلید عمومی — هرگز کلید خصوصی).

کاربرد:
    python3 tools/replace-product-key.py <public.pem> [--keys-dir <dir>] [--key-id sup-202611] [--dry-run]

چه می‌کند؟
  ۱) متن PEM را می‌خواند و مطمئن می‌شود «کلید عمومی» است (اگر PRIVATE باشد، رد می‌کند).
  ۲) اثر انگشت SHA-256 کلید در قالب DER (SubjectPublicKeyInfo) را حساب می‌کند —
     همان چیزی که محصول هنگام بارگذاری بررسی می‌کند.
  ۳) فایل را روی <keys-dir>/<key-id>.pem می‌نویسد.
  ۴) ردیف همان key-id در manifest.json را به‌روزرسانی می‌کند (sha256 + file + algorithm)
     و ردیف‌های دیگر را دست‌نخورده می‌گذارد.

چرا امن است؟ کلید عمومی راز نیست؛ فقط «امضاکنندهٔ مجاز» را تعیین می‌کند. کلید خصوصی
هیچ‌جا خوانده یا نوشته نمی‌شود.
"""
import argparse
import base64
import hashlib
import json
import re
import shutil
import sys
from pathlib import Path


def der_from_pem(text: str) -> bytes:
    m = re.search(r"-----BEGIN ([A-Z ]+)-----(.*?)-----END \1-----", text, re.S)
    if not m:
        raise SystemExit("✖ در متن، بلوک PEM پیدا نشد.")
    label, body = m.group(1).strip(), m.group(2)
    if "PRIVATE" in label.upper():
        raise SystemExit("✖ این یک کلید خصوصی است. هرگز کلید خصوصی را در مخزن یا چت نگذارید.")
    if label.upper() not in ("PUBLIC KEY", "RSA PUBLIC KEY"):
        raise SystemExit(f"✖ برچسب PEM پشتیبانی نمی‌شود: {label}")
    return base64.b64decode("".join(body.split()))


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("pem")
    ap.add_argument("--keys-dir", default="/var/tmp/app/TotallCollectionSoftware/inventory/src/Inventory.Licensing/Keys")
    ap.add_argument("--key-id", default="sup-202611")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()

    src = Path(a.pem)
    if not src.exists():
        raise SystemExit(f"✖ فایل نیست: {src}")
    text = src.read_text(encoding="utf-8")

    der = der_from_pem(text)
    fp = hashlib.sha256(der).hexdigest()

    keys_dir = Path(a.keys_dir)
    manifest_path = keys_dir / "manifest.json"
    if not manifest_path.exists():
        raise SystemExit(f"✖ manifest.json پیدا نشد در {keys_dir}")

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    rows = manifest.get("keys", [])
    row = next((r for r in rows if str(r.get("keyId", "")).lower() == a.key_id.lower()), None)
    if row is None:
        raise SystemExit(f"✖ ردیف «{a.key_id}» در manifest نیست (برای چرخش کلید، ردیف تازه بسازید).")

    old_fp = row.get("sha256")
    pem_name = row.get("file") or f"{a.key_id}.pem"
    target = keys_dir / pem_name

    print(f"■ کلید فعلی در manifest : {old_fp}")
    print(f"■ کلید تازه (محاسبه‌شده): {fp}")
    if old_fp and old_fp.lower() == fp.lower():
        print("✔ اثر انگشت یکی است — چیزی برای تغییر نیست.")
        return 0

    print(f"■ فایل مقصد          : {target}")
    if a.dry_run:
        print("(dry-run) تغییری نوشته نشد.")
        return 0

    # نسخه پشتیبان از کلید و manifest (بیرون از مخزن، کنار فایل کلید)
    backup = src.parent / f"{pem_name}.bak-{old_fp[:12] if old_fp else 'nokey'}"
    if target.exists() and not backup.exists():
        shutil.copy2(target, backup)
        print(f"■ نسخه پشتیبان کلید قبلی: {backup}")

    target.write_text(text if text.endswith("\n") else text + "\n", encoding="utf-8")

    row["sha256"] = fp
    row["file"] = pem_name
    row.setdefault("algorithm", "RS256")
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    print("✔ کلید عمومی و ردیف manifest به‌روزرسانی شد.")
    print("  قدم بعد: بیلد Release محصول (کلید داخل Inventory.Licensing.dll جاسازی می‌شود) و انتشار روی نصب مشتری.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
