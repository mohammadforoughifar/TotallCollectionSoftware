"""قراردادهای چاپ «فروغ آریا» (شرکت ۱ و ۲) — یک منبع سربرگ برای هر سه چاپ.

گزارش کارفرما (سه مورد جدا، یک ریشه):

۱) «نامهٔ صادرهٔ شرکت ۱ با سربرگ شرکت ۱۰۰۵ چاپ شد» — کد فقط «وجود فایل» را می‌سنجید؛
   فایل ثبت‌شده روی رکورد شرکت (یا Watermark قالب) می‌توانست از خانوادهٔ شرکت دیگری باشد.
۲) «نامهٔ داخلی و صورتجلسهٔ شرکت ۱ هم باید از همان فایل سربرگ نامهٔ صادره چاپ شوند» —
   `wwwroot/uploads/letterheads/forough-letterhead.pdf`.
۳) «مقدارهای تاریخ/شماره/پیوست شرکت ۱ روی خط‌های نقطه‌چینِ همان هنر بنشینند» —
   سربرگ شرکت ۱ یک صفحهٔ US Letter است، پس هندسهٔ کالیبرهٔ شرکت‌های دیگر (یا کادرهای
   MRT شرکت ۱ که y منفی دارند) روی آن معنا ندارد.

این‌ها قرارداد «متن مبدأ» هستند (بدون بیلد هم قابل اجرا): هر تغییری که این قاعده‌ها را
بشکند، همین‌جا شکست می‌خورد.
"""

from __future__ import annotations

import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
# درخت «مخزن اصلی» و درخت «پوشهٔ راه‌حل»؛ هرکدام که موجود باشد سنجیده می‌شود. (پوشهٔ src
# در ریشهٔ مخزن در نسخهٔ canonical وجود ندارد؛ تست‌های این فایل نباید به آن وابسته باشند.)
TREES = [tree for tree in (ROOT / "src", ROOT / "inventory" / "src") if tree.exists()]


def office(tree: Path) -> Path:
    return tree / "Inventory.Api" / "Services" / "Office"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class SourceCase(unittest.TestCase):
    """بررسی الگو در فایل مبدأ، با پیام شکست کوتاه (assertIn کل فایل را چاپ می‌کند)."""

    def has(self, needle: str, source: str, why: str = "") -> None:
        if needle in source:
            return
        detail = f" ({why})" if why else ""
        self.fail(f"الگوی لازم در متن مبدأ نیست: {needle!r}{detail}")

    def lacks(self, needle: str, source: str, why: str = "") -> None:
        if needle not in source:
            return
        detail = f" ({why})" if why else ""
        self.fail(f"الگوی ممنوع در متن مبدأ هست: {needle!r}{detail}")

    def has_equal(self, actual, expected, why: str = "") -> None:
        if actual == expected:
            return
        detail = f" ({why})" if why else ""
        self.fail(f"مقدار مورد انتظار {expected!r} است، ولی {actual!r} بود{detail}")


class ForoughFamilyTests(SourceCase):
    """هر سه سرویس چاپ باید یک قاعده و یک فایل برای فروغ آریا داشته باشند."""

    def test_resolver_is_the_single_source_of_the_family(self):
        for tree in TREES:
            with self.subTest(tree=tree.parts[-3]):
                resolver = read(office(tree) / "PrintAssetResolver.cs")
                # شناسه‌های پیش‌فرض و فایل، فقط یک‌جا تعریف می‌شوند.
                self.has("internal static readonly int[] DefaultForoughCompanyIds = { 1, 2 };", resolver)
                self.has(
                    'internal const string ForoughLetterheadFileName = "forough-letterhead.pdf";', resolver)
                self.has("internal static bool IsForoughCompany(int companyId, string? configuredIds)", resolver)

    def test_all_three_prints_read_the_same_file(self):
        for tree in TREES:
            with self.subTest(tree=tree.parts[-3]):
                outgoing = read(office(tree) / "Outgoing" / "OutgoingLetterPrintService.cs")
                inner = read(office(tree) / "InnerLetterPrintService.cs")
                minutes = read(office(tree) / "MeetingMinutesPrintService.cs")
                for name, source in (("outgoing", outgoing), ("inner", inner), ("minutes", minutes)):
                    with self.subTest(service=name):
                        # مسیر فایل از ثابت مشترک ساخته می‌شود، نه با نام رشته‌ای جدا در هر سرویس.
                        self.has("PrintAssetResolver.ForoughLetterheadFileName", source)
                        self.has("PrintAssetResolver.IsForoughCompany", source)

    def test_outgoing_never_even_looks_at_another_source_for_forough(self):
        for tree in TREES:
            with self.subTest(tree=tree.parts[-3]):
                service = read(office(tree) / "Outgoing" / "OutgoingLetterPrintService.cs")
                # مسیر اختصاصی فروغ آریا، و شاخهٔ جدا از منطق «فایل ثبت‌شده/قالب».
                self.has("private string? ResolveForoughLetterhead(bool isA5)", service)
                self.has("var isForough = withLetterhead && selectedCompanyId is int foroughCandidate", service)
                # نامهٔ شرکت ۱ همیشه چیدمان‌محور چاپ می‌شود (فارغ از DesignDrivenCompanyIds).
                self.has("designLayout = withLetterhead && selectedCompanyId is > 0", service)
                # نبودِ فایل = پیام فارسی روشن، نه چاپ روی صفحهٔ سفید.
                self.has("سربرگ شرکت «", service)
                self.has("چاپ متوقف شد", service)

    def test_meta_values_sit_on_the_printed_dotted_lines(self):
        for tree in TREES:
            with self.subTest(tree=tree.parts[-3]):
                service = read(office(tree) / "Outgoing" / "OutgoingLetterPrintService.cs")
                # هندسهٔ سه ردیفِ هنر، در یک place و با مقیاس A5.
                self.has("private readonly record struct ArtworkMetaGeometry(", service)
                self.has("FirstRowTop: 28f * scale,", service)
                self.has("RowPitch: 27.2f * scale,", service)
                self.has("ValueLeft: 22f * scale,", service)
                self.has("ValueRight: 84f * scale,", service)
                # PaddingLeft بیرون عرض داخلی است؛ لبهٔ راست نباید به‌جای عرض استفاده شود.
                self.lacks(".PaddingLeft(geometry.ValueLeft).Width(geometry.ValueRight)", service)
                self.has("geometry.ValueRight - geometry.ValueLeft", service)
                self.has("var fields = ReadDeclaredMetaFields(companyId is > 0", service)
                self.has("OutgoingLetterMetaPainter.Draw(host, fields, number, date, attachment);", service)
                painter = read(office(tree) / "Outgoing" / "OutgoingLetterMetaPainter.cs")
                self.has(".Width(field.Width).Height(field.Height)", painter)
                self.has("text.DirectionFromLeftToRight()", painter)
                self.has("private void AddArtworkMetaValues(IContainer host, bool isA5,", service)
                # فقط برای فروغ آریا؛ شرکت‌های دیگر همان مسیر کالیبره را دارند.
                self.has("var alignMetaToArtwork = hasLetterhead && letterheadCompanyId is int metaCompany", service)
                self.has("if (alignMetaToArtwork)", service)

    def test_minutes_puts_meta_on_the_artwork_only_for_forough(self):
        for tree in TREES:
            with self.subTest(tree=tree.parts[-3]):
                minutes = read(office(tree) / "MeetingMinutesPrintService.cs")
                self.has("var metaOnArtwork = isForough && hasLetterhead;", minutes)
                self.has("BuildArtworkMetaRows(design, number, meetingDate, attachmentText, hasLetterhead, isA5)", minutes)
                # ترتیب ردیف‌ها همان ترتیب چاپ‌شده روی هنر: تاریخ، شماره، پیوست.
                self.has("Add(design.MeetingDate, date, 0);", minutes)
                self.has("const float rowPitch = 27.2f;", minutes)
                self.has("const float firstRowTop = 28f;", minutes)
                self.has("Add(design.Number, number, 1);", minutes)
                self.has("Add(design.Attachment, attachment, 2);", minutes)

    def test_second_page_carries_the_letterhead_band(self):
        """صفحه‌های دوم و بعد سربرگ دارند — به‌صورت «برش نوار»، نه تکرار کل صفحه."""
        for tree in TREES:
            with self.subTest(tree=tree.parts[-3]):
                outgoing = read(office(tree) / "Outgoing" / "OutgoingLetterPrintService.cs")
                inner = read(office(tree) / "InnerLetterPrintService.cs")
                minutes = read(office(tree) / "MeetingMinutesPrintService.cs")
                for name, source, clip in (
                        ("outgoing", outgoing, "graphics.IntersectClip(new XRect(strip.X, strip.Y, strip.Width, strip.Height));"),
                        ("inner", inner, "g.IntersectClip(new XRect(strip.X, strip.Y, strip.Width, strip.Height));"),
                        ("minutes", minutes, "gfx.IntersectClip(new XRect(band.X, band.Y, band.Width, band.Height));")):
                    with self.subTest(service=name):
                        self.has(clip, source)
                        # برش با Save/Restore بسته می‌شود، وگرنه محتوای صفحه هم بریده می‌شد.
                        self.has("graphics.Restore();" if name == "outgoing" else ".Restore();", source)
                        self.has("PrintAssetResolver.LetterheadBandRect(", source)

    def test_settings_declare_the_family_once_and_globally(self):
        for tree in TREES:
            with self.subTest(tree=tree.parts[-3]):
                settings = read(tree / "Inventory.Api" / "appsettings.json")
                self.has('"ForoughCompanyIds": "1,2"', settings)
                # کلید «یک‌جا» برای هر سه سرویس خوانده می‌شود (نه در سه بخش جدا).
                self.has_equal(settings.count('"ForoughCompanyIds"'), 1, "کلید باید فقط یک‌جا باشد")
                self.has('"DesignDrivenCompanyIds": "1003,1"', settings)
                self.has('"TemplateLetterheadCompanyIds": "1003,1"', settings)


class PrintNeverFailsHardTests(SourceCase):
    """گزارش کارفرما: «نامهٔ طولانی با خطای DocumentLayoutException چاپ نمی‌شد».

    قرارداد: متن بلند نباید چاپ را کامل بشکند؛ نردبان جایگزین باید تا «متن ساده» برود و
    فقط اگر هیچ‌چیز چاپ نشود، پیام فارسی روشن برگردد.
    """

    def service(self, tree: Path) -> str:
        return read(office(tree) / "Outgoing" / "OutgoingLetterPrintService.cs")

    def test_recovery_ladder_has_three_layout_attempts_plus_a_plain_fallback(self):
        for tree in TREES:
            with self.subTest(tree=tree.parts[-3]):
                service = self.service(tree)
                self.has("private byte[] BuildContentPdfWithRecovery(", service)
                self.has('("normal", false, true),', service)
                self.has('("tail-unlocked", false, false),', service)
                self.has('("compact", true, false)', service)
                self.has("private byte[] BuildPlainFallbackPdf(", service)
                # هر نوع خطای موتور چاپ (نه فقط DocumentLayoutException) به پلهٔ بعد می‌رود.
                self.has("catch (Exception ex) when (ex is not OperationCanceledException)", service)

    def test_header_reservations_never_use_a_fixed_height(self):
        """Height قطعی روی ناحیهٔ سربرگ = «تضاد محدودیت» با محتوای متغیر."""
        for tree in TREES:
            with self.subTest(tree=tree.parts[-3]):
                service = self.service(tree)
                self.has("page.Header().ContentFromLeftToRight().MinHeight(contentTop)", service)
                # هیچ رزرو سربرگی در مسیر «از دل طرح» با Height قطعی ساخته نمی‌شود.
                for line in service.splitlines():
                    if "page.Header()" in line or ".Height(isA5 ? 150 : 160)" in line:
                        self.lacks(".Height(isA5 ? 150 : 160)", line,
                                         "رزرو سربرگ باید MinHeight باشد، نه Height قطعی")
                self.has("page.Header().MinHeight(headerReserve)", read(
                    office(tree) / "MeetingMinutesPrintService.cs"))
                self.has("page.Header().MinHeight(contentTop)", read(
                    office(tree) / "InnerLetterPdfRenderer.cs"))

    def test_signer_block_is_capped_not_fixed(self):
        """عرض ثابت ۲۲۰pt×۲ در A5 از عرض کاغذ بیشتر می‌شد و رندر را می‌شکست."""
        for tree in TREES:
            with self.subTest(tree=tree.parts[-3]):
                service = self.service(tree)
                self.has(".AlignLeft().MaxWidth(signerBlockWidth * signerColumns, Unit.Point)", service)
                self.lacks(".AlignLeft().Width(signerBlockWidth * signerColumns)", service)

    def test_controller_shows_the_persian_message_not_the_engine_error(self):
        for tree in TREES:
            with self.subTest(tree=tree.parts[-3]):
                controller = read(tree / "Inventory.Api" / "Controllers" / "Office" / "OutgoingLettersController.cs")
                self.has("catch (OutgoingLetterPrintException ex)", controller)
                self.has("return StatusCode(500, new { message = ex.Message });", controller)


if __name__ == "__main__":
    unittest.main()
