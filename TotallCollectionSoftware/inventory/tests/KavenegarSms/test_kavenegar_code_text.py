"""The Persian meaning of every provider code the UI can display.

A returned number on its own ("13") tells the user nothing, and it collides with an
unrelated local enum value, so the detail views must spell the code out. These checks
pin the mapping to the two published tables of the provider's REST documentation:
"جدول ۱ - کدهای برگشتی" and "جدول ۲ - وضعیت پیامک‌ها".
"""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]

# Documented return codes (table 1). Values are substrings that must appear in the text,
# chosen so a wrong row cannot satisfy the assertion.
RETURN_CODES = {
    200: 'تایید', 400: 'ناقص', 401: 'غیرفعال', 402: 'ناموفق', 403: 'API-Key',
    404: 'نامشخص', 405: 'Get/Post', 406: 'اجباری', 407: 'دسترسی', 409: 'پاسخگویی',
    411: 'دریافت\u200cکننده', 412: 'ارسال\u200cکننده', 413: 'طول پیام', 414: 'حجم درخواست',
    415: 'اندیس شروع', 416: 'IP', 417: 'تاریخ', 418: 'اعتبار', 419: 'آرایه',
    420: 'لینک', 422: 'کاراکتر نامناسب', 424: 'الگو', 426: 'سرویس پیشرفته',
    427: 'سطح دسترسی', 428: 'تماس تلفنی', 429: 'IP', 431: 'جداکننده',
    432: 'الگو', 451: 'بازهٔ زمانی', 501: 'آزمایشی', 607: 'تگ',
}

# Documented message statuses (table 2); 50 comes from the official SDK enum.
MESSAGE_STATUSES = {
    1: 'صف ارسال', 2: 'زمان\u200cبندی', 4: 'مخابرات', 5: 'مخابرات', 6: 'سرشماره',
    10: 'رسیده به گیرنده', 11: 'خاموش', 13: 'لغو', 14: 'بلاک', 50: 'دریافت\u200cشده',
    100: 'نامعتبر',
}


def read(path):
    return (ROOT / path).read_text(encoding='utf-8')


def arms(body):
    """Map every `code => "text"` arm of a switch expression, expanding `4 or 5` lists."""
    found = {}
    for keys, text in re.findall(r'^\s*([\d\s or]+?)\s*=>\s*"([^"]+)"', body, re.MULTILINE):
        for key in re.findall(r'\d+', keys):
            found[int(key)] = text
    return found


class KavenegarCodeTextContracts(unittest.TestCase):
    source = read('src/Inventory.Shared/KavenegarCodeText.cs')

    def table(self, name):
        body = self.source.split(f'public static string? {name}(int ', 1)[1].split('};', 1)[0]
        return arms(body)

    def test_every_documented_return_code_has_persian_text(self):
        mapped = self.table('ReturnCode')
        self.assertEqual(sorted(mapped), sorted(RETURN_CODES))
        for code, needle in RETURN_CODES.items():
            self.assertIn(needle, mapped[code], f'return code {code}')

    def test_every_documented_message_status_has_persian_text(self):
        mapped = self.table('MessageStatus')
        self.assertEqual(sorted(mapped), sorted(MESSAGE_STATUSES))
        for status, needle in MESSAGE_STATUSES.items():
            self.assertIn(needle, mapped[status], f'message status {status}')

    def test_status_13_is_a_provider_cancellation_with_a_refund(self):
        # The one the user actually hit. It must not read like a local validation error.
        text = self.table('MessageStatus')[13]
        self.assertIn('لغو', text)
        self.assertIn('برگشت', text)
        self.assertNotIn('دستور کار', text)

    def test_the_two_tables_never_overlap_so_one_number_is_unambiguous(self):
        # Describe() routes by a 200 boundary; that is only sound while the tables stay apart.
        self.assertTrue(all(code >= 200 for code in self.table('ReturnCode')))
        self.assertTrue(all(status <= 100 for status in self.table('MessageStatus')))
        self.assertFalse(set(self.table('ReturnCode')) & set(self.table('MessageStatus')))

    def test_an_undocumented_code_is_reported_as_unknown_not_guessed(self):
        describe = self.source.split('public static string Describe(', 1)[1]
        self.assertIn('code >= 200 ? ReturnCode(code) : MessageStatus(code)', describe)
        self.assertIn('"کد ناشناختهٔ درگاه پیامک"', describe)

    def test_work_order_details_explain_the_code_and_every_local_status(self):
        appearance = read('src/Inventory.Client/Pages/ItAssets/WorkOrderSmsAppearance.cs')
        self.assertIn('using Inventory.Shared;', appearance)
        self.assertIn('KavenegarCodeText.Describe(code)', appearance)
        statuses = read('src/Inventory.Shared/Dtos/WorkOrderSmsDtos.cs')
        statuses = statuses.split('enum WorkOrderSmsStatus', 1)[1].split('}', 1)[0]
        detail = appearance.split('public static string Detail(', 1)[1].split('};', 1)[0]
        for name in re.findall(r'(\w+)\s*=\s*\d+', statuses):
            self.assertIn(f'WorkOrderSmsStatus.{name} =>', detail, f'{name} has no Persian explanation')
        ui = read('src/Inventory.Client/Pages/ItAssets/WorkOrders.razor')
        area = ui.split('<details class="wo-block mb-3 no-print" open>', 1)[1].split('</details>', 1)[0]
        self.assertIn('var returnedReason = WorkOrderSmsAppearance.FailureReason(item);', area)
        self.assertIn('@returnedReason', area)
        # The compact badge and its returned number stay exactly as they were.
        self.assertIn('<bdi class="num" dir="ltr">@(returnedCode.Value)</bdi>', area)

    def test_repair_details_reuse_the_same_table(self):
        ui = read('src/Inventory.Client/Pages/Repairs/Repairs.razor')
        section = ui.split('<section class="border rounded p-3 mb-3" aria-label="وضعیت پیامک مشتری">', 1)[1]
        section = section.split('</section>', 1)[0]
        self.assertIn('var returnedReason = SmsFailureReason(sms);', section)
        self.assertIn('@returnedReason', section)
        self.assertIn('KavenegarCodeText.Describe(code)', ui)

    def test_the_offline_projects_compile_the_shared_table(self):
        project = ET.parse(ROOT / 'tests/WorkOrderSmsUiOffline/WorkOrderSmsUiOffline.csproj')
        includes = [c.get('Include') for c in project.findall('.//Compile')]
        self.assertIn('../../src/Inventory.Shared/KavenegarCodeText.cs', includes)
        self.assertIn("ROOT / 'src/Inventory.Shared/KavenegarCodeText.cs',",
                      read('tests/WorkOrderSmsUiHarness/run.py'))


if __name__ == '__main__':
    unittest.main()
