"""استخراج متن کامل از فایل‌های آفیس (Word .docx / Excel .xlsx) شامل جدول‌ها."""
from __future__ import annotations

import io
import logging
import zipfile
import re
import xml.etree.ElementTree as ET

log = logging.getLogger("ocr.office")

W_NS = "{http://schemas.openxmlformats.org/wordprocessingml/2006/main}"


# --------------------------------------------------------------------------
# 1) Word (.docx) — استخراج کامل: بدنه، جدول‌ها، سربرگ/پاصفحه
# --------------------------------------------------------------------------
def _w_texts(el: ET.Element) -> str:
    """متن تمام گره‌های w:t زیر یک المان، با فاصله بین پاراگراف‌ها."""
    parts: list[str] = []
    for t in el.iter(W_NS + "t"):
        txt = t.text or ""
        parts.append(txt)
    return "".join(parts)


def _w_paragraphs(el: ET.Element) -> list[str]:
    out: list[str] = []
    for p in el.iter(W_NS + "p"):
        text = "".join((t.text or "") for t in p.iter(W_NS + "t")).strip()
        if text:
            out.append(text)
    return out


def extract_docx(data: bytes) -> str:
    """متن همه‌ٔ پاراگراف‌ها و جدول‌های سند (و سربرگ/پاصفحه)."""
    try:
        import docx  # python-docx
    except ImportError:
        docx = None

    if docx is not None:
        try:
            document = docx.Document(io.BytesIO(data))
            lines: list[str] = []

            def add_runs(p) -> None:
                t = "".join(run.text for run in p.runs).strip()
                if t:
                    lines.append(t)

            for p in document.paragraphs:
                add_runs(p)
            for table in document.tables:
                for row in table.rows:
                    cells = [c.text.strip().replace("\n", " ") for c in row.cells]
                    lines.append(" | ".join(c for c in cells if c))
            # سربرگ/پاصفحه
            for section in document.sections:
                for p in list(section.header.paragraphs) + list(section.footer.paragraphs):
                    add_runs(p)
            text = "\n".join(lines)
            if text.strip():
                return text.strip()
        except Exception as e:
            log.warning("python-docx ناموفق بود؛ پشتیبان XML استفاده می‌شود: %s", e)

    # پشتیبان: تجزیهٔ مستقیم XML (بدون کتابخانه)
    return _docx_xml_fallback(data)


def _docx_xml_fallback(data: bytes) -> str:
    try:
        with zipfile.ZipFile(io.BytesIO(data)) as z:
            lines: list[str] = []
            names = [n for n in z.namelist()
                     if n.startswith("word/") and n.endswith(".xml") and
                     (n.startswith("word/document") or n.startswith("word/header") or n.startswith("word/footer"))]
            for n in names:
                try:
                    root = ET.fromstring(z.read(n))
                    for p in root.iter(W_NS + "p"):
                        text = _w_texts(p).strip()
                        if text:
                            lines.append(text)
                except Exception:
                    continue
            return "\n".join(lines).strip()
    except Exception as e:
        log.warning("پشتیبان docx نیز ناموفق بود: %s", e)
        return ""


# --------------------------------------------------------------------------
# 2) Excel (.xlsx) — همهٔ شیت‌ها با openpyxl
# --------------------------------------------------------------------------
def extract_xlsx(data: bytes) -> str:
    try:
        import openpyxl
    except ImportError:
        return _xlsx_xml_fallback(data)
    try:
        wb = openpyxl.load_workbook(io.BytesIO(data), data_only=False, read_only=True)
        lines: list[str] = []
        for ws in wb.worksheets:
            rows_text = []
            for row in ws.iter_rows(values_only=True):
                vals = []
                for cell in row:
                    if cell is None:
                        continue
                    v = str(cell).strip()
                    if v:
                        vals.append(v)
                if vals:
                    rows_text.append(" | ".join(vals))
            if rows_text:
                lines.append(f"[شیت: {ws.title}]")
                lines.extend(rows_text)
        wb.close()
        text = "\n".join(lines)
        if text.strip():
            return text.strip()
    except Exception as e:
        log.warning("openpyxl ناموفق بود؛ پشتیبان XML استفاده می‌شود: %s", e)
    return _xlsx_xml_fallback(data)


def _xlsx_xml_fallback(data: bytes) -> str:
    """پشتیبان بدون کتابخانه: sharedStrings + مقادیر سلول‌ها."""
    try:
        with zipfile.ZipFile(io.BytesIO(data)) as z:
            shared: list[str] = []
            try:
                root = ET.fromstring(z.read("xl/sharedStrings.xml"))
                for si in root.iter():
                    if si.tag.endswith("}si"):
                        shared.append("".join((t.text or "") for t in si.iter() if t.tag.endswith("}t")))
            except KeyError:
                pass
            lines: list[str] = []
            for n in sorted(z.namelist()):
                if not (n.startswith("xl/worksheets/") and n.endswith(".xml")):
                    continue
                root = ET.fromstring(z.read(n))
                sheet_lines = []
                for row in root.iter():
                    if not row.tag.endswith("}row"):
                        continue
                    vals = []
                    for c in row:
                        if not c.tag.endswith("}c"):
                            continue
                        v = c.find(".//{http://schemas.openxmlformats.org/spreadsheetml/2006/main}v")
                        val = (v.text or "").strip() if v is not None else ""
                        t = c.get("t")
                        if t == "s" and val.isdigit() and int(val) < len(shared):
                            val = shared[int(val)]
                        if val:
                            vals.append(val)
                    if vals:
                        sheet_lines.append(" | ".join(vals))
                if sheet_lines:
                    lines.extend(sheet_lines)
            return "\n".join(lines).strip()
    except Exception as e:
        log.warning("پشتیبان xlsx ناموفق بود: %s", e)
        return ""


# --------------------------------------------------------------------------
# نقطهٔ ورود یکتا
# --------------------------------------------------------------------------
def extract_office(data: bytes, ext: str) -> tuple[str, str]:
    """(متن، نوع) برای فایل‌های docx/xlsx؛ نوع خالی یعنی پشتیبانی‌نشده."""
    ext = ext.lower()
    if ext == ".docx":
        return extract_docx(data), "Word"
    if ext == ".xlsx":
        return extract_xlsx(data), "Excel"
    return "", ""
