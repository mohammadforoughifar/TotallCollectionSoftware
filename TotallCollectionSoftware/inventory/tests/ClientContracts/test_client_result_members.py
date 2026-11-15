"""نگهبان «عضوِ ناموجود روی نتیجهٔ سرویس» در کلاینت (خطای CS1061).

چرا این تست هست: در ادغام‌ها دو طرفِ یک ویژگی با هم جابه‌جا می‌شوند — مثلاً سیاست
«تصویرِ درون‌متن ایمیل ضمیمه نشود» کنار گذاشته شد و `InlineImages/InlineCount` از
`EmailAttachmentTransfer` حذف شد، ولی در یک صفحهٔ `.razor` هنوز `transfer.InlineCount`
مانده بود. نتیجه: بیلد `Inventory.Client` با CS1061 می‌شکست، در حالی که هیچ تستی آن را
نمی‌گرفت (تست‌های پایتون فقط قرارداد فایل‌ها را می‌سنجند).

این تست برای هر «نتیجهٔ سرویس» در کلاینت، هر `متغیر.عضو` را که در `.razor`/`.cs`
استفاده شده با اعضای اعلام‌شدهٔ همان کلاس مقایسه می‌کند. اگر روزی عضوی حذف/تغییر نام
داد و جایی از آن استفاده ماند، همین‌جا قرمز می‌شود — نه در IDE کاربر.

اجرا:
    python3 -m unittest discover -s inventory/tests/ClientContracts -p 'test_*.py'
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
TREES = [ROOT / "inventory" / "src" / "Inventory.Client", ROOT / "src" / "Inventory.Client"]

# چگونگی ساختنِ هر «نتیجه» در کد، و نامِ کلاسی که اعضا باید در آن باشد.
# الگو: (نام کلاس، regex برای تشخیص متغیری که از این نوع است)
RESULT_TYPES = (
    ("EmailAttachmentTransfer", r"var\s+(\w+)\s*=\s*await\s+EmailAttachmentTransferService\.CollectAsync"),
    ("EmailLetterBody", r"var\s+(\w+)\s*=\s*await\s+EmailLetterBodyBuilder\.BuildAsync"),
    ("EmailLetterContentResult", r"var\s+(\w+)\s*=\s*await\s+EmailLetterContent\.BuildAsync"),
)


def client_files(tree: Path):
    return [p for p in tree.rglob("*") if p.suffix in (".razor", ".cs")]


def declared_members(tree: Path, class_name: str) -> set[str]:
    """اعضای عمومیِ اعلام‌شده در کلاسی با این نام (در همان درخت)."""
    members: set[str] = set()
    pattern = re.compile(rf"\bclass\s+{re.escape(class_name)}\b")
    for path in (tree / "Services").rglob("*.cs"):
        text = path.read_text(encoding="utf-8", errors="ignore")
        # کلاس باید در همین فایل اعلام شده باشد (وگرنه اعضای فایل دیگری شمرده می‌شود).
        if not pattern.search(text):
            continue
        members.update(re.findall(r"public\s+[\w<>\[\],?.\s]+?\b(\w+)\s*\{\s*get", text))
        members.update(re.findall(r"public\s+[\w<>\[\],?.\s]+?\b(\w+)\s*(?:=|;)", text))
        members.update(re.findall(r"public\s+[\w<>\[\],?.\s]+?\b(\w+)\s*\(", text))
        return members
    return members


class ClientResultMemberTests(unittest.TestCase):
    def test_every_used_member_exists_on_the_result_class(self):
        checked = 0
        for tree in TREES:
            if not tree.is_dir():
                continue
            for class_name, declaration in RESULT_TYPES:
                members = declared_members(tree, class_name)
                if not members:
                    continue  # این نوع در این درخت نیست (دو درخت کمی متفاوت‌اند)
                sources = client_files(tree)
                for path in sources:
                    text = path.read_text(encoding="utf-8", errors="ignore")
                    variables = set(re.findall(declaration, text))
                    for variable in variables:
                        # اعضای استفاده‌شده روی همین متغیر
                        for used in re.findall(rf"\b{re.escape(variable)}\.(\w+)", text):
                            checked += 1
                            with self.subTest(tree=tree.parent.name, file=path.name,
                                              type=class_name, member=used):
                                self.assertIn(
                                    used, members,
                                    f"{class_name} عضو «{used}» را ندارد؛ "
                                    f"{path} با آن بیلد نمی‌شود (CS1061)")
        self.assertGreater(checked, 0, "هیچ عضوی بررسی نشد — الگوهای تشخیص متغیر کهنه شده‌اند")

    def test_inline_images_policy_is_not_referenced_any_more(self):
        """سیاست «تصویر درون‌متن ضمیمه نشود» کنار گذاشته شد؛ ارجاع باقی‌مانده = CS1061."""
        for tree in TREES:
            if not tree.is_dir():
                continue
            for path in client_files(tree):
                text = path.read_text(encoding="utf-8", errors="ignore")
                with self.subTest(tree=tree.parent.name, file=path.name):
                    self.assertNotIn("transfer.InlineCount", text)
                    self.assertNotIn("transfer.InlineImages", text)


if __name__ == "__main__":
    unittest.main(verbosity=2)
