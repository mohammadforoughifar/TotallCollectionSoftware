"""جای دونقطهٔ برچسب‌ها: «موضوع:» و نه «:موضوع».

ایراد کارفرما: «موضوع نامه ... یعنی دو تا نقطه (:) به جای این که جلوی کلمه باشند عقب کلمه
قرار می‌گیرند». دونقطه (:، U+003A) یک کاراکتر «خنثی» در الگوریتم دوجهته (bidi) است؛ اگر
آخرین کاراکتر یک متن باشد، جهتش را از جهت پایهٔ پاراگراف می‌گیرد و می‌تواند سمت راست کلمهٔ
فارسی — یعنی «قبل» از آن — چاپ شود.

راه‌حل همان راه‌حل جاافتادهٔ همین پروژه است (`RichLetterPdfRenderer.MarkRightToLeft`): دونقطه
بین دو کاراکتر قویِ راست‌به‌چپ (RLM، U+200F) قرار می‌گیرد، پس همیشه سمت چپ کلمه چاپ می‌شود
و هیچ فاصله یا دونقطهٔ اضافه‌ای هم ساخته نمی‌شود.

سندباکس `dotnet` ندارد، پس این تست‌ها «قرارداد» کد را می‌سنجند، نه خروجی PDF را.
"""

from __future__ import annotations

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
# هر دو درخت چاپ: src (سرور اصلی) و inventory/src — باید یکسان رفتار کنند.
TREES = [ROOT / "src", ROOT / "inventory" / "src"]

RLM = "\u200f"  # Right-to-Left Mark


def office(tree: Path) -> Path:
    return tree / "Inventory.Api" / "Services" / "Office"


class LabelColonPlacementTests(unittest.TestCase):
    def template(self, tree: Path) -> str:
        return (office(tree) / "LetterPrintTemplate.cs").read_text(encoding="utf-8")

    def outgoing(self, tree: Path) -> str:
        return (office(tree) / "Outgoing" / "OutgoingLetterPrintService.cs").read_text(
            encoding="utf-8")

    def minutes(self, tree: Path) -> str:
        return (office(tree) / "MeetingMinutesPrintService.cs").read_text(encoding="utf-8")

    def inner(self, tree: Path) -> str:
        return (office(tree) / "InnerLetterPdfRenderer.cs").read_text(encoding="utf-8")

    def test_shared_helper_exists_and_is_the_single_place_for_label_colons(self):
        """یک کمک‌تابع مشترک، با RLM واقعی (U+200F) و نه یک رشتهٔ فرار خام."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                template = self.template(tree)
                self.assertIn('internal const string Rlm = "\\u200F";', template)
                self.assertIn("internal static string RtlLabel(string? label", template)
            # خودِ کاراکتر RLM نویسهٔ بی‌عرض است و در منبع نباید هارد‌کد شود.
                self.assertNotIn(RLM, template, "RLM باید با \\u200F نوشته شود، نه به‌صورت خام")

    def test_colon_sits_between_two_strong_right_to_left_marks(self):
        """دونقطه بعد از کلمه و بین دو RLM ⇒ همیشه سمت چپ کلمه چاپ می‌شود."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                template = self.template(tree)
                self.assertIn('? Rlm + value + ":" + Rlm', template)
                # دونقطهٔ سرگردانِ ابتدای برچسب («:حاضرین») هم به پایان منتقل می‌شود.
                self.assertIn("value.StartsWith(':')", template)
                self.assertIn("value.TrimEnd(':', '\\uFF1A')", template)

    def test_info_block_labels_are_printed_through_the_helper(self):
        """«موضوع» و «عطف به شماره» با همان تابع چاپ می‌شوند، نه با $"{label}:"."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                template = self.template(tree)
                self.assertIn(".Text(RtlLabel(row.Label))", template)
                self.assertNotIn('.Text($"{row.Label}:")', template)
                # متن برچسب عوض نشده است (فقط محل دونقطه اصلاح شده).
                outgoing = self.outgoing(tree)
                self.assertIn('new("موضوع", title, BoldValue: true)', outgoing)
                self.assertIn('new("عطف به شماره", externalReference)', outgoing)

    def test_copy_to_label_uses_the_helper(self):
        """«رونوشت:» هم همان مسیر را می‌رود (همهٔ شرکت‌ها)."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                template = self.template(tree)
                self.assertIn('var label = RtlLabel("رونوشت");', template)
                self.assertNotIn('const string label = "رونوشت:";', template)
        # عرض تخمینی ستون برچسب همان «رونوشت:» است تا دونقطه جایی برای نشستن داشته باشد.
        for tree in TREES:
            with self.subTest(tree=tree.name):
                self.assertIn('"رونوشت:".Length * fontSize * 0.58f + fontSize',
                              self.template(tree))

    def test_design_meta_labels_use_the_helper(self):
        """برچسب‌های شماره/تاریخ/پیوست در لایهٔ سربرگ نامهٔ صادره."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                outgoing = self.outgoing(tree)
                self.assertIn(".Text(LetterPrintTemplate.RtlLabel(label))", outgoing)
                self.assertNotIn('.Text($"{label}:")', outgoing)

    def test_minutes_captions_do_not_gain_a_colon(self):
        """برچسب‌های آمادهٔ قالب MRT («حاضرین») فقط محصور می‌شوند، دونقطه اضافه نمی‌گیرند."""
        for tree in TREES:
            with self.subTest(tree=tree.name):
                minutes = self.minutes(tree)
                self.assertIn("ensureTrailingColon: false", minutes)
                self.assertIn('LetterPrintTemplate.RtlLabel("بندها و تصمیمات")', minutes)
                self.assertIn('LetterPrintTemplate.RtlLabel("امضای حاضرین")', minutes)
                self.assertNotIn('Text("بندها و تصمیمات:")', minutes)
                self.assertNotIn('Text("امضای حاضرین:")', minutes)

    def test_inner_letter_hamesh_uses_the_helper(self):
        for tree in TREES:
            with self.subTest(tree=tree.name):
                inner = self.inner(tree)
                self.assertIn('LetterPrintTemplate.RtlLabel("هامش")', inner)
                self.assertNotIn('Text("هامش:")', inner)

    def test_no_label_is_left_with_a_bare_trailing_colon(self):
        """هیچ برچسبی در ماژول‌های چاپ با الگوی قدیمی $"...:" چاپ نمی‌شود."""
        pattern = re.compile(r'\.Text\(\$"\{[^}]+\}:"\)')
        for tree in TREES:
            for path in office(tree).rglob("*.cs"):
                text = path.read_text(encoding="utf-8")
                with self.subTest(tree=tree.name, file=path.name):
                    self.assertEqual(pattern.findall(text), [],
                                     f"{path.name}: دونقطه باید از RtlLabel بیاید")


if __name__ == "__main__":
    unittest.main()
