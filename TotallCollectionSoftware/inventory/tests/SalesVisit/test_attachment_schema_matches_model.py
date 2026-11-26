"""قرارداد «مدل EF پیوست‌ها = اسکیمای واقعی جدول AppAttachments».

خطای واقعی (گزارش کارفرما): فهرست «سفارش‌های من» در ماژول ویزیتوری با
`Invalid column name 'DocumentId'` و HTTP 400 باز نمی‌شد. علت: موجودیت
`AppAttachment` ستونی به نام `DocumentId` داشت که هیچ مایگریشن/خودتعمیری آن را
نمی‌ساخت؛ EF در هر SELECT آن ستون را می‌خواست و SQL Server خطا می‌داد.

این تست هیچ کامپایلر/دیتابیسی لازم ندارد و فقط متن را می‌خواند:
  • هر پراپرتی `AppAttachment` باید یا در مایگریشن SquashedInitial باشد،
    یا در خودتعمیر VisAttachmentSchemaV1 اضافه شود.
  • ستون‌های خودتعمیر هم باید در فهرست Columns باشند تا روی دیتابیس موجود ساخته شوند.

اجرا:
    python3 -m unittest discover -s inventory/tests/SalesVisit -p 'test_attachment_schema_matches_model.py'
"""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "src"
ENTITY = ROOT / "Inventory.Api/Entities/System/ArchiveAndAttachments.cs"
SCHEMA_ATT = ROOT / "Inventory.Api/Data/VisAttachmentSchemaV1.cs"
SQUASHED = ROOT / "Inventory.Api/Migrations/20260911100108_SquashedInitial.cs"
SERVICE = ROOT / "Inventory.Api/Services/SalesVisit/VisAttachmentService.cs"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class AttachmentSchemaMatchesModelTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.entity = read(ENTITY)
        cls.schema = read(SCHEMA_ATT)
        cls.squashed = read(SQUASHED)

        body = cls.entity[cls.entity.index("public class AppAttachment"):
                           cls.entity.index("public class AppAttachmentAccessLog")]
        # فقط پراپرتی‌های ذخیره‌شدنی (get/set) — پراپرتی‌های محاسباتی ستون نیستند.
        cls.model_columns = re.findall(r"public\s+[\w\?<>\[\]]+\s+(\w+)\s*\{\s*get;\s*set;", body)

        start = cls.squashed.index('name: "AppAttachments",')
        block = cls.squashed[start:cls.squashed.index('name: "ArchiveFolders",', start)]
        cls.migration_columns = set(re.findall(r"(\w+) = table\.Column<", block))
        # ستون‌هایی که همان مایگریشن دم‌چسب‌شده بعداً با AddColumn به این جدول می‌افزاید.
        cls.migration_columns |= set(re.findall(
            r'name:\s*"(\w+)",\s*table:\s*"AppAttachments"', cls.squashed))

        cls.repaired_columns = set(re.findall(r'\(\s*"(\w+)"\s*,\s*"[^"]*"\s*,\s*"[^"]*"\s*\)',
                                              cls.schema[cls.schema.index("Columns ="):]))

    def test_every_model_column_exists_in_database(self):
        """ستونی که در مدل هست ولی هیچ‌جا ساخته نمی‌شود = خطای Invalid column name."""
        missing = [c for c in self.model_columns
                   if c not in self.migration_columns and c not in self.repaired_columns]
        self.assertEqual([], missing,
                         "این ستون‌ها در مدل EF هستند ولی مایگریشن/خودتعمیر آن‌ها را نمی‌سازد")

    def test_document_id_is_not_modelled(self):
        """DocumentId هرگز ستون این جدول نبود؛ مالکیت پیوست با UploaderUserId است."""
        self.assertNotIn("DocumentId", self.model_columns)
        self.assertIn("UploaderUserId", self.model_columns)

    def test_service_does_not_reference_removed_column(self):
        svc = read(SERVICE)
        self.assertNotIn("DocumentId", svc)
        self.assertIn("a.UploaderUserId == userId", svc)


if __name__ == "__main__":
    unittest.main()
