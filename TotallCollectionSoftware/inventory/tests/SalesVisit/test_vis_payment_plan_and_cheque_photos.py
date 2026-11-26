"""قرارداد «پرداخت ترکیبی + عکس چک» در ماژول فروش و ویزیتوری.

بدون نیاز به .NET SDK، مرورگر، API یا پایگاه‌داده:
    python3 -m unittest discover -s inventory/tests/SalesVisit -p 'test_*.py' -v

موارد تحت پوشش (خواستهٔ کارفرما):
  ۱) نوع پرداخت سفارش ویزیتور **ترکیبی** است و همان چهار جزء فاکتور خرید/فروش را دارد:
     نقدی + چک + اقساط + نسیه، با باقی‌ماندهٔ هر جزء.
  ۲) پیوست عکس چک: آپلود در فرم، انتقال به سفارش پس از ذخیره، و اعتبارسنجی سرور
     («پرداخت چکی بدون عکس چک ثبت نمی‌شود»).
  ۳) کارتابل مالی: عکس‌ها دیده و هر مدرک تأیید/رد می‌شود؛ تأیید مالی نهایی تا
     تصمیم‌نگرفتن روی مدارک متوقف می‌ماند.
  ۴) اسکیمای افزودنی (idempotent) و مایگریشن دست‌نویس؛ هیچ سند قدیمی بازنویسی نمی‌شود.
"""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2] / "src"

VISIT_FORM = ROOT / "Inventory.Client/Pages/SalesVisit/VisitForm.razor"
VISIT_DETAIL = ROOT / "Inventory.Client/Pages/SalesVisit/VisitDetail.razor"
FINANCE_MODAL = ROOT / "Inventory.Client/Pages/SalesVisit/VisFinanceModal.razor"
FINANCE_QUEUE = ROOT / "Inventory.Client/Pages/SalesVisit/FinanceQueue.razor"
CHEQUE_PHOTOS = ROOT / "Inventory.Client/Shared/VisChequePhotos.razor"
CLIENT_API = ROOT / "Inventory.Client/Services/SalesVisitApi.cs"

SERVICE = ROOT / "Inventory.Api/Services/SalesVisit/VisOrderService.cs"
ATTACH_SERVICE = ROOT / "Inventory.Api/Services/SalesVisit/VisAttachmentService.cs"
CONTROLLER = ROOT / "Inventory.Api/Controllers/SalesVisit/SalesVisitController.cs"
ATTACH_CONTROLLER = ROOT / "Inventory.Api/Controllers/System/ArchiveController.cs"
GUARD = ROOT / "Inventory.Api/Services/AttachmentGuard.cs"
CARTABLE = ROOT / "Inventory.Api/Services/Cartable/CartableService.cs"

ORDER_ENTITY = ROOT / "Inventory.Api/Entities/SalesVisit/VisOrder.cs"
ATTACH_ENTITY = ROOT / "Inventory.Api/Entities/System/ArchiveAndAttachments.cs"
DTOS = ROOT / "Inventory.Shared/Dtos/SalesVisitDtos.cs"
PLAN = ROOT / "Inventory.Shared/SalesVisitPaymentPlan.cs"

SCHEMA_ORDERS = ROOT / "Inventory.Api/Data/SalesVisitSchemaV1.cs"
SCHEMA_PAY = ROOT / "Inventory.Api/Data/VisPaymentPlanSchemaV1.cs"
SCHEMA_ATT = ROOT / "Inventory.Api/Data/VisAttachmentSchemaV1.cs"
MIGRATION = ROOT / "Inventory.Api/Migrations/20261125120000_AddVisPaymentPlan.cs"
DB_INIT = ROOT / "Inventory.Api/Data/DbInitializer.cs"
PROGRAM = ROOT / "Inventory.Api/Program.cs"


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class SharedPaymentPlanTests(unittest.TestCase):
    """قاعدهٔ مشترک کلاینت/سرور."""

    @classmethod
    def setUpClass(cls):
        cls.plan = read(PLAN)

    def test_plan_exposes_every_component_and_validator(self):
        for member in ("MethodOf(", "MethodOfAmounts(", "RemainingAfterPayments(",
                       "ValidateCheques(", "ValidateInstallments(", "ValidateRemainder(",
                       "DescribeParts("):
            with self.subTest(member=member):
                self.assertIn(member, self.plan)

    def test_more_than_one_component_is_mixed(self):
        self.assertIn("if (parts > 1) return PaymentMethod.Mixed;", self.plan)

    def test_cheque_needs_number_amount_and_due_date(self):
        self.assertIn("شماره چک ردیف", self.plan)
        self.assertIn("مبلغ چک ردیف", self.plan)
        self.assertIn("سررسید چک ردیف", self.plan)

    def test_installments_need_amount_and_due_date(self):
        self.assertIn("مبلغ قسط", self.plan)
        self.assertIn("سررسید قسط", self.plan)

    def test_remainder_needs_credit_and_due_date(self):
        self.assertIn("به نسیه می‌رود", self.plan)
        self.assertIn("تاریخ سررسید نسیه را وارد کنید", self.plan)


class ModelAndSchemaTests(unittest.TestCase):
    """ستون‌ها، اسکیمای افزودنی و مایگریشن."""

    @classmethod
    def setUpClass(cls):
        cls.entity = read(ORDER_ENTITY)
        cls.att_entity = read(ATTACH_ENTITY)
        cls.schema = read(SCHEMA_PAY)
        cls.schema_att = read(SCHEMA_ATT)
        cls.orders_schema = read(SCHEMA_ORDERS)
        cls.migration = read(MIGRATION)
        cls.db_init = read(DB_INIT)

    def test_order_entity_has_payment_columns(self):
        for column in ("CashAmount", "CashType", "TrsAccountId", "DueDate",
                       "ChequesJson", "InstallmentsJson", "PaymentCashAmount", "PaymentChequeSum",
                       "PaymentInstallmentSum", "PaymentCreditRemainder",
                       "FinanceAttachmentsApproved", "FinanceAttachmentDecidedAt",
                       "FinanceAttachmentDecidedBy", "FinanceAttachmentNote"):
            with self.subTest(column=column):
                self.assertIn(column, self.entity)

    def test_attachment_entity_has_finance_decision(self):
        for member in ("Category", "CompanyId", "FinancialStatus", "FinancialDecidedAt",
                       "FinancialDecidedBy", "FinancialNote", "Size"):
            with self.subTest(member=member):
                self.assertIn(member, self.att_entity)
        self.assertIn("VisAttachmentFinancialStatus", self.att_entity)

    def test_schema_repair_is_additive_and_idempotent(self):
        for src in (self.schema, self.schema_att):
            self.assertIn("COL_LENGTH", src, "SQL Server باید فقط ستون غایب را اضافه کند")
            self.assertIn("pragma_table_info", src, "SQLite باید ستون غایب را اضافه کند")
            self.assertNotIn("DROP", src)
            self.assertNotIn("DELETE FROM", src)
            self.assertNotIn("UPDATE ", src, "هیچ سند/مبلغی بازنویسی نمی‌شود")

    def test_fresh_visorders_table_already_contains_new_columns(self):
        for column in ("[CashAmount]", "[ChequesJson]", "[PaymentCreditRemainder]",
                       "[FinanceAttachmentsApproved]"):
            with self.subTest(column=column):
                self.assertIn(column, self.orders_schema)

    def test_migration_is_hand_written_and_safe(self):
        self.assertIn('[Migration("20261125120000_AddVisPaymentPlan")]', self.migration)
        self.assertIn("VisPaymentPlanSchemaV1.SqlServer", self.migration)
        self.assertIn("VisAttachmentSchemaV1.SqlServer", self.migration)
        self.assertIn("NotSupportedException", self.migration, "Down نباید داده را حذف کند")

    def test_db_initializer_runs_both_schema_repairs(self):
        self.assertIn("VisPaymentPlanSchemaV1.EnsureAsync(db)", self.db_init)
        self.assertIn("VisAttachmentSchemaV1.EnsureAsync(db)", self.db_init)

    def test_attachment_service_is_registered(self):
        self.assertIn("IVisAttachmentService", read(PROGRAM))


class ServerBehaviourTests(unittest.TestCase):
    """قواعد سرور — منبع حقیقت، نه فقط اعتبارسنجی کلاینت."""

    @classmethod
    def setUpClass(cls):
        cls.svc = read(SERVICE)
        cls.attach = read(ATTACH_SERVICE)
        cls.controller = read(CONTROLLER)

    def test_save_parses_and_persists_components(self):
        for member in ("ApplyPaymentPlanAsync", "ChequesJson = ToJson(cheques)",
                       "InstallmentsJson = ToJson(installments)", "PaymentChequeSum = chequeSum",
                       "PaymentInstallmentSum = installmentSum",
                       "PaymentCreditRemainder = Math.Max(0, remaining)"):
            with self.subTest(member=member):
                self.assertIn(member, self.svc)

    def test_server_validates_cheques_installments_and_remainder(self):
        self.assertIn("SalesVisitPaymentPlan.ValidateCheques", self.svc)
        self.assertIn("SalesVisitPaymentPlan.ValidateInstallments", self.svc)
        self.assertIn("SalesVisitPaymentPlan.ValidateRemainder", self.svc)

    def test_cash_needs_active_company_owned_account(self):
        self.assertIn("حساب صندوق یا بانک را انتخاب کنید", self.svc)
        self.assertIn("حساب صندوق یا بانک باید متعلق به شرکت همین سفارش باشد", self.svc)
        self.assertIn("نوع حساب با نوع دریافت نقدی انتخاب‌شده مطابقت ندارد", self.svc)

    def test_submit_requires_cheque_photo(self):
        self.assertIn("PendingChequeAttachmentsAsync", self.svc)
        self.assertIn("نوع پرداخت چکی است؛ عکس چک را در بخش پیوست بارگذاری کنید.", self.svc)

    def test_finance_approve_blocks_until_documents_decided(self):
        self.assertIn("عکس چک این سفارش توسط مالی رد شده است", self.svc)
        self.assertIn("پیش از تأیید مالی، عکس چک‌ها را بررسی و تأیید کنید", self.svc)

    def test_finance_approve_inherits_agent_components_when_finance_sends_none(self):
        self.assertIn("planCheques", self.svc)
        self.assertIn("clientSentParts", self.svc)
        self.assertIn("cmd.Cheques = planCheques;", self.svc)
        self.assertIn("cmd.Installments = planInstallments;", self.svc)
        self.assertIn("cmd.PartsReviewed", self.svc)
        self.assertIn("PartsReviewed = true", read(FINANCE_MODAL),
                      "مالی اجزای دیده‌شده را صریحاً تأیید می‌کند")

    def test_finance_approve_derives_sale_method_from_real_parts(self):
        """روش سند فروش نباید از کلاینت پذیرفته شود؛ از اجزای واقعی بازمحاسبه می‌شود."""
        self.assertIn("cmd.PaymentMethod = PaymentMix.Derive(cashSent, chequeSent, installSent, creditSent);",
                      self.svc)
        self.assertIn("var cashSent = cmd.PaymentMethod == PaymentMethod.Cash", self.svc)

    def test_finance_decide_rejects_without_reason(self):
        self.assertIn("دلیل رد مدرک الزامی است", self.svc)
        self.assertIn("FinanceDecideAsync", self.svc)

    def test_draft_attachments_move_after_id_exists(self):
        # پیش‌نویس با RefId = -1 آپلود می‌شود؛ انتقال باید پس از SaveChanges باشد
        # وگرنه شناسهٔ سفارش صفر است و عکس گم می‌شود.
        save_index = self.svc.index("await ApplyPaymentPlanAsync(actor, entity, cmd);")
        move_index = self.svc.index("MoveDraftAttachmentsAsync(entity.Id")
        changes_index = self.svc.index("await _db.SaveChangesAsync();", save_index)
        self.assertLess(save_index, changes_index)
        self.assertLess(changes_index, move_index,
                        "انتقال پیوست باید بعد از ذخیرهٔ سفارش (ساخته‌شدن شناسه) باشد")

    def test_attachment_service_rejects_executables_and_gigantic_files(self):
        for ext in (".jpg", ".png", ".pdf", ".tif"):
            with self.subTest(ext=ext):
                self.assertIn(ext, self.attach)
        self.assertIn("مگابایت است", self.attach)
        self.assertNotIn(".exe", self.attach)
        self.assertNotIn(".bat", self.attach)

    def test_attachment_service_scopes_drafts_to_the_uploader(self):
        self.assertIn("a.UploaderUserId == userId", self.attach)
        self.assertIn("DraftRefId", self.attach)

    def test_finance_decide_endpoint_is_permission_guarded(self):
        self.assertIn('[HttpPost("{id:int}/finance-decide")]', self.controller)
        guard_start = self.controller.index("FinanceDecide(")
        guard_body = self.controller[guard_start:guard_start + 500]
        self.assertIn('HasAnyAsync("FinanceView", "Approve")', guard_body)
        self.assertIn("Forbid()", guard_body)


class AttachmentEndpointTests(unittest.TestCase):
    """endpoint مشترک پیوست‌ها: دسته، پیش‌نویس و نگهبان دسترسی."""

    @classmethod
    def setUpClass(cls):
        cls.controller = read(ATTACH_CONTROLLER)
        cls.guard = read(GUARD)

    def test_upload_accepts_category(self):
        self.assertIn("string? category = null", self.controller)
        self.assertIn("Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim()", self.controller)

    def test_list_filters_by_category(self):
        self.assertIn("a.Category == categoryFilter", self.controller)

    def test_draft_list_requires_explicit_flag_and_owner(self):
        self.assertIn("isSalesVisitDraft", self.controller)
        self.assertIn("if (!draft || MyUserId <= 0)", self.controller)
        self.assertIn("a.UploaderUserId == MyUserId", self.controller)

    def test_cash_tab_toggle_zeroes_cash_component(self):
        """خاموش‌کردن تب «نقدی» باید جزء نقدی را صفر کند، نه اینکه مبلغ تایپ‌شده باقی بماند."""
        for path in (VISIT_FORM, FINANCE_MODAL):
            src = read(path)
            self.assertIn(": useCash ? Fa.ParseDecimal(cashAmountText) : 0;", src, path.name)

    def test_finance_modal_declares_mixed_when_parts_combine(self):
        modal = read(FINANCE_MODAL)
        self.assertIn("SalesVisitPaymentPlan.MethodOf(", modal.split("private PaymentMethod DeclaredMethod")[1][:400])

    def test_has_cheque_part_is_amount_based_not_only_pure_cheque(self):
        """«ترکیبی با چک» هم باید نشان چک و جعبهٔ عکس چک داشته باشد."""
        dtos = read(DTOS)
        start = dtos.index("public bool HasChequePart =>")
        body = dtos[start:dtos.index(";", dtos.index("EffectivePaymentMethod == PaymentMethod.Cheque", start))]
        self.assertIn("ChequeCount > 0", body)
        self.assertIn("ChequeSum > PaymentMix.Tolerance / 2", body)

    def test_guard_protects_sales_visit(self):
        self.assertIn('"SalesVisit"', self.guard)
        self.assertIn("owner.Value == userId", self.guard)
        self.assertIn('p.Action == "Approve" || p.Action == "FinanceView"', self.guard)

    def test_thumb_endpoint_is_cheap_and_cacheable(self):
        """کاشی‌های شبکهٔ مدارک باید بندانگشتی کوچک و کش‌شدنی بگیرند، نه فایل کامل + گردانه."""
        self.assertIn('[HttpGet("thumb/{id:int}")]', self.controller)
        self.assertIn("private, max-age=300", self.controller)
        self.assertIn("Size = new Size(Math.Max(1, (int)(w * ratio)), Math.Max(1, (int)(h * ratio)))",
                      self.controller)

    def test_thumb_respects_draft_owner_and_confidential_flag(self):
        thumb = self.controller[self.controller.index('[HttpGet("thumb/{id:int}")]'):
                                self.controller.index('/// <summary>\n    /// پیش‌نمایش Word/Excel')]
        self.assertIn("CanOpenRecord(a)", thumb)
        self.assertIn("PASSWORD_CONFIRM_REQUIRED", thumb)

    def test_client_grids_use_thumb_endpoint(self):
        for path in (CHEQUE_PHOTOS, FINANCE_MODAL, VISIT_DETAIL):
            src = read(path)
            self.assertIn("/api/attachments/thumb/", src, f"{path.name} باید بندانگشتی بخواند")
            self.assertNotIn("/api/attachments/preview/{a.Id}?access_token", src)


class CartableTests(unittest.TestCase):
    """کارتابل مالی باید سفارش‌های دارای عکس چک تأییدنشده را بالا بیاورد."""

    @classmethod
    def setUpClass(cls):
        cls.cartable = read(CARTABLE)

    def test_finance_pending_docs_item(self):
        self.assertIn('Kind = "finance-docs-pending"', self.cartable)
        self.assertIn("a.FinancialStatus == Inventory.Api.Data.VisAttachmentFinancialStatus.Pending", self.cartable)
        self.assertIn("/sales/finance-queue", self.cartable)

    def test_pending_docs_only_for_approved_orders(self):
        self.assertIn("o.Id == a.RefId && o.Status == VisOrderStatus.Approved", self.cartable)


class DraftCleanupTests(unittest.TestCase):
    """پیوست‌های پیش‌نویس رهاشده نباید روی سرور بمانند."""

    def test_expire_stale_cleans_drafts_through_service(self):
        svc = read(SERVICE)
        self.assertIn("CleanupDraftAttachmentsAsync()", svc)
        self.assertIn("DraftRetentionDays", svc)
        # حذف باید از مسیر سرویس (رکورد + فایل دیسک) باشد، نه RemoveRange خام
        start = svc.index("private async Task CleanupDraftAttachmentsAsync")
        body = svc[start:svc.index("\n    }\n", start)]
        self.assertIn("AsNoTracking()", body)
        self.assertIn("_attachments.DeleteAsync(", body)
        self.assertNotIn("RemoveRange", body)


class ClientUiTests(unittest.TestCase):
    """فرم ویزیتور: چهار جزء هم‌زمان + عکس چک."""

    @classmethod
    def setUpClass(cls):
        cls.form = read(VISIT_FORM)
        cls.markup = cls.form[: cls.form.index("<style>")]
        cls.detail = read(VISIT_DETAIL)
        cls.modal = read(FINANCE_MODAL)
        cls.queue = read(FINANCE_QUEUE)
        cls.photos = read(CHEQUE_PHOTOS)
        cls.api = read(CLIENT_API)

    # ---------- فرم ----------
    def test_form_has_four_independent_components(self):
        for comp in ("cash", "cheque", "installment", "credit"):
            with self.subTest(comp=comp):
                self.assertIn(f'data-vis-pay-comp="{comp}"', self.markup)
        self.assertIn("ToggleCash", self.form)
        self.assertIn("ToggleCheque", self.form)
        self.assertIn("ToggleInstallment", self.form)
        self.assertIn("ToggleCredit", self.form)

    def test_form_shows_remaining_after_each_component(self):
        for marker in ("data-vis-remain", "data-vis-remain-cash",
                       "data-vis-rem-cheque", "data-vis-rem-inst", "data-vis-credit-amount"):
            with self.subTest(marker=marker):
                self.assertIn(marker, self.markup)

    def test_form_has_multiple_cheque_rows_with_bank_and_quick_bank(self):
        self.assertIn('data-vis-add-cheque data-hotkey="f2"', self.markup)
        self.assertIn("data-vis-cheque-row=", self.markup)
        self.assertIn("<SearchSelectStr", self.markup)
        self.assertIn("QuickBankModal", self.form)
        self.assertIn("InvoiceFormKey", self.form)
        self.assertIn("data-vis-cheque-bank", self.form, "مودال افزودن بانک چک")

    def test_form_has_installment_booklet(self):
        self.assertIn("data-vis-build-inst", self.markup)
        self.assertIn("data-vis-inst-row=", self.markup)
        self.assertIn("BuildInstallments", self.form)

    def test_form_has_cheque_photo_upload_inside_cheque_component(self):
        self.assertIn("<VisChequePhotos", self.markup)
        self.assertIn("data-cheque-photos", self.photos)
        self.assertIn("data-cheque-photo-input", self.photos)
        self.assertIn("<InputFile", self.photos,
                      "بلازور فقط برای InputFile رویداد فایل می‌سازد")
        self.assertIn("Category", self.photos)
        self.assertIn("VisAttachmentCategories.Cheque", self.photos)

    def test_form_blocks_save_when_cheque_has_no_photo(self):
        self.assertIn("عکس چک را در بخش «عکس چک‌ها» بارگذاری کنید", self.form)

    def test_form_sends_attachments_and_payment_parts(self):
        self.assertIn("model.ChequeAttachmentIds", self.form)
        self.assertIn("model.CashAmount", self.form)
        self.assertIn("model.TrsAccountId", self.form)
        self.assertIn("model.DueDate", self.form)

    def test_form_loads_existing_payment_plan_when_editing(self):
        self.assertIn("LoadPaymentPlanFrom(o)", self.form)
        self.assertIn("LoadPaymentPlanFrom", self.form)

    def test_form_resets_plan_for_next_order(self):
        self.assertIn("ResetPaymentPlan();", self.form)

    def test_quick_payment_method_label_uses_effective_method(self):
        self.assertIn("@PaymentMethodText", self.markup)
        self.assertIn("SalesVisitPaymentPlan.MethodOf", self.form)

    # ---------- جزئیات ----------
    def test_detail_shows_component_breakdown(self):
        for marker in ("data-vis-payment-detail", "data-vis-cheque-badge",
                       "data-vis-detail-docs", "data-vis-detail-doc="):
            with self.subTest(marker=marker):
                self.assertIn(marker, self.detail)
        self.assertIn("نسیه (باقی‌مانده)", self.detail)

    # ---------- کارتابل مالی ----------
    def test_finance_modal_shows_and_decides_each_photo(self):
        for marker in ("data-fin-docs", "data-fin-doc-grid", "data-fin-doc=",
                       "data-fin-doc-ok=", "data-fin-doc-reject=", "data-fin-approve-docs"):
            with self.subTest(marker=marker):
                self.assertIn(marker, self.modal)
        self.assertIn("FinanceDecideAsync", self.modal)
        self.assertIn("DecideDocAsync(", self.modal)
        self.assertIn("DecideDocsAsync(", self.modal)

    def test_finance_modal_keeps_add_cheque_hotkey_contract(self):
        self.assertIn('data-fin-add-cheque data-hotkey="f2"', self.modal)
        self.assertIn("data-fin-submit", self.modal)

    def test_finance_modal_prefills_from_agent_components(self):
        self.assertIn("Order.Cheques.Count > 0", self.modal)
        self.assertIn("Order.Installments", self.modal)
        self.assertIn("Order.CashAmount", self.modal)
        self.assertIn("Order.TrsAccountId", self.modal)

    def test_finance_queue_shows_cheque_and_document_counts(self):
        self.assertIn("data-fin-cheque-count", self.queue)
        self.assertIn("data-fin-doc-count", self.queue)
        self.assertIn("o.EffectivePaymentTitle", self.queue)

    def test_client_api_has_finance_decide(self):
        self.assertIn("Task<VisOrderDto> FinanceDecideAsync(int id, VisFinanceDecision decision)", self.api)
        self.assertIn('finance-decide', self.api)

    # ---------- تراکم/طراحی ----------
    def test_payment_block_does_not_add_scrollable_tables(self):
        self.assertNotIn('class="table-responsive"', self.markup,
                         "جزء اقساط هم نباید کانتینر اسکرول‌پذیر بسازد")
        self.assertNotIn("ConstrainToModal=", self.form,
                         "گزینه‌های مخصوص فاکتور در فرم ویزیتور فعال نمی‌شوند")
        self.assertNotIn("ShowAllOnFocus=", self.form)


class DtoContractTests(unittest.TestCase):
    """DTO مشترک: یک منبع برای کلاینت و سرور."""

    @classmethod
    def setUpClass(cls):
        cls.dtos = read(DTOS)

    def test_order_dto_exposes_parts_and_attachments(self):
        for member in ("CashAmount", "CashType", "TrsAccountId", "DueDate", "Cheques",
                       "Installments", "HasPaymentPlan", "ChequeSum", "InstallmentSum",
                       "CreditRemainder", "EffectivePaymentMethod", "PaymentSummary",
                       "Attachments", "ChequeAttachments", "HasChequePart", "ChequeCount"):
            with self.subTest(member=member):
                self.assertIn(member, self.dtos)

    def test_finance_decision_dto(self):
        for member in ("Approved", "Comment", "AttachmentIds", "RejectOrder", "ChequeOnly"):
            with self.subTest(member=member):
                self.assertIn(member, self.dtos)

    def test_attachment_dto_has_decision_state(self):
        for member in ("FinanceApproved", "DecisionTitle", "DecisionCss", "IsCheque", "CanPreview"):
            with self.subTest(member=member):
                self.assertIn(member, self.dtos)

    def test_payment_plan_file_imports_dtos_namespace(self):
        """CS0246: ChequeDto/InstallmentDto در Inventory.Shared.Dtos هستند.

        فایل SalesVisitPaymentPlan در namespace ریشه (Inventory.Shared) است و
        global using فقط همان را می‌آورد؛ پس using صریح Dtos لازم است.
        """
        plan = read(PLAN)
        self.assertIn("using Inventory.Shared.Dtos;", plan)
        # مطمئن شویم واقعاً به تایپ‌های آن namespace نیاز دارد
        self.assertIn("ChequeDto", plan)
        self.assertIn("InstallmentDto", plan)

    def test_no_duplicate_member_in_attachment_dto(self):
        """CS0102: VisOrderAttachmentDto نباید عضو تکراری داشته باشد.

        قبلاً CanPreview دو بار تعریف شده بود (محاسبه‌ای + قابل‌نوشتن) و بیلد
        با «already contains a definition» شکست می‌خورد.
        """
        start = self.dtos.index("public class VisOrderAttachmentDto")
        end = self.dtos.index("public static class VisAttachmentCategories")
        body = self.dtos[start:end]
        self.assertEqual(body.count("public bool CanPreview"), 1,
                         "CanPreview باید دقیقاً یک تعریف داشته باشد")
        self.assertIn("public bool CanPreview =>", body,
                      "نسخهٔ محاسبه‌ای (IsImage/PDF) باید بماند")

    def test_categories_are_shared_constants(self):
        self.assertIn('public const string Cheque = "Cheque";', self.dtos)
        self.assertIn("public const int DraftRefId = -1;", self.dtos)


# تایپ‌ها/نان‌اسپیس‌هایی که در عبارت‌های مارک‌آپ مجازند (using سراسری یا @using فایل)
_RAZOR_TYPES = {
    "Math", "String", "DateTime", "Decimal", "Boolean", "Int32", "Console",
    "CashType", "TreasuryAccountKind", "PaymentMethod", "VisOrderStatus",
    "Fa", "VisUi", "VisAttachmentCategories", "VisPaymentPart", "System",
    "Enumerable", "Task", "List", "Guid", "Uri", "Inventory", "Layout",
}

class RazorCompileSafetyTests(unittest.TestCase):
    """خطاهای بیلد Razor که با کپی‌کردن مارک‌آپ از فرم فاکتور پیش آمدند.

    هیچ کامپایلری در این محیط نیست، پس همین دو قاعدهٔ ساده جلوی برگشتشان را می‌گیرند.
    """

    @classmethod
    def setUpClass(cls):
        cls.form = read(VISIT_FORM)
        cls.detail = read(VISIT_DETAIL)

    def test_no_single_at_media_inside_style(self):
        """CS0103 'media': در <style> باید @@media نوشت، نه @media.

        Razor تکِ '@' را درون بلوک استایل به‌عنوان عبارت C# می‌خواند و «media»
        را شناسهٔ ناموجود می‌بیند.
        """
        for path, name in ((VISIT_FORM, "VisitForm"), (VISIT_DETAIL, "VisitDetail")):
            src = read(path)
            for m in re.finditer(r"<style>(.*?)</style>", src, re.S):
                block = m.group(1)
                bare = [ln for ln in block.split("\n")
                        if re.search(r"(?<!@)@[a-zA-Z]", ln) and not re.search(r"@@[a-zA-Z]", ln)]
                self.assertEqual([], bare, f"{name}: '@' تنها در <style> به C# تفسیر می‌شود")

    def test_cash_kind_identifier_is_declared(self):
        """CS0103 'CashKind': مارک‌آپ CashKind می‌خواهد؛ باید عضو هم‌نام وجود داشته باشد.

        فیلد اصلی cashKind است (حروف کوچک) و C# حساس به بزرگی/کوچکی حرف است.
        """
        self.assertIn("CashKind", self.form, "مارک‌آپ از CashKind استفاده می‌کند")
        self.assertRegex(self.form, r"private\s+CashType\s+CashKind\s*=>",
                         "عضو CashKind باید تعریف شده باشد")

    def test_csharp_expressions_use_declared_identifiers(self):
        """CS0103: داخل @(...) و @Identifier هر نام باید در @code تعریف شده باشد.

        برخلاف بررسی «همهٔ شناسه‌های مارک‌آپ»، اینجا فقط عبارت‌های C# بررسی
        می‌شوند تا پارامترهای کامپوننت (Items، Value، ...) هشدار ندهند.
        """
        for path, name in ((VISIT_FORM, "VisitForm"), (VISIT_DETAIL, "VisitDetail")):
            src = read(path)
            cut = src.find("@code")
            code, markup = src[cut:], src[:cut]
            markup = re.sub(r"@\*.*?\*@", "", markup, flags=re.S)
            markup = re.sub(r"<style>.*?</style>", "", markup, flags=re.S)

            declared = set(re.findall(
                r"\b(?:private|public|protected|internal)\s+"
                r"(?:static\s+|readonly\s+|override\s+|async\s+)*"
                r"[\w\?<>,\[\]\.]+\s+([A-Za-z_]\w*)", code))

            # فقط عبارت‌های صریح C#: @( ... ) و @SomeIdentifier (نه داخل نقل‌قول)
            exprs = re.findall(r"@\(([^()]*(?:\([^()]*\)[^()]*)*)\)", markup)
            exprs += re.findall(r"(?<![\w@])@([A-Z][A-Za-z0-9_]*)\b(?!\s*[=\w])", markup)

            missing = set()
            for e in exprs:
                for ident in re.findall(r"(?<![.\w\"])([A-Z][A-Za-z0-9_]*)\b", e):
                    if ident in declared or ident in _RAZOR_TYPES:
                        continue
                    if ident.lower() in {d.lower() for d in declared}:
                        missing.add(ident)   # اختلاف فقط در بزرگی/کوچکی حرف
            self.assertEqual(set(), missing, f"{name}: شناسه‌های تعریف‌نشده {sorted(missing)}")


if __name__ == "__main__":
    unittest.main()
