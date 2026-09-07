/* RADIS-HR — پل ارتباطی JS برای نسخهٔ Blazor
   شامل: گروه‌بندی سه‌رقمی ورودی‌های ریالی (هم‌رفتار با rial-inputs.js)، ذخیره‌سازی محلی توکن و چاپ. */
(() => {
  "use strict";

  const selector = "input[data-rial]";

  const toLatinDigits = value => String(value ?? "").replace(/[۰-۹]/g, digit =>
    String("۰۱۲۳۴۵۶۷۸۹".indexOf(digit))
  );

  const raw = value => {
    const digits = toLatinDigits(value).replace(/[^\d]/g, "");
    return digits.replace(/^0+(?=\d)/, "");
  };

  const format = value => raw(value).replace(/\B(?=(\d{3})+(?!\d))/g, ",");

  const digitCount = value => (toLatinDigits(value).match(/\d/g) || []).length;

  const caretAfterDigits = (value, count) => {
    if (!count) return 0;
    let seen = 0;
    for (let index = 0; index < value.length; index += 1) {
      if (/\d/.test(value[index])) seen += 1;
      if (seen === count) return index + 1;
    }
    return value.length;
  };

  function formatInput(input, preserveCaret = false) {
    if (!input?.matches?.(selector)) return;
    const before = input.value;
    const selection = preserveCaret && document.activeElement === input
      ? digitCount(before.slice(0, input.selectionStart ?? before.length))
      : null;
    const after = format(before);
    if (before !== after) input.value = after;
    if (selection !== null && !input.readOnly) {
      const caret = caretAfterDigits(after, selection);
      input.setSelectionRange(caret, caret);
    }
  }

  function formatAll(root = document) {
    (root || document).querySelectorAll(selector).forEach(input => formatInput(input));
  }

  document.addEventListener("input", event => {
    if (event.target.matches?.(selector)) formatInput(event.target, true);
  }, true);

  document.addEventListener("focusin", event => {
    if (event.target.matches?.(selector)) formatInput(event.target, true);
  });

  window.RADIS_RIAL = Object.freeze({ raw, format, formatAll });

  window.radisInterop = {
    formatRials: () => formatAll(),
    rawValue: value => raw(value),
    formatValue: value => format(value),

    getItem: key => window.localStorage.getItem(key),
    setItem: (key, value) => window.localStorage.setItem(key, value),
    removeItem: key => window.localStorage.removeItem(key),

    printPayslip: elementId => {
      const payslip = document.getElementById(elementId);
      if (!payslip || !payslip.innerHTML.trim()) {
        window.alert("ابتدا فیش حقوقی موردنظر را انتخاب کنید.");
        return;
      }
      const printWindow = window.open("", "_blank", "width=1100,height=800");
      if (!printWindow) {
        window.alert("مرورگر پنجره چاپ را مسدود کرده است. اجازه Pop-up را فعال کنید.");
        return;
      }
      printWindow.document.open();
      printWindow.document.write(`<!doctype html>
        <html lang="fa" dir="rtl"><head><meta charset="utf-8"><title>فیش حقوقی A5</title>
        <style>
          @page{size:210mm 148mm;margin:0}
          @font-face{font-family:"Vazirmatn";src:url("/fonts/Vazirmatn-Regular.woff2") format("woff2");font-weight:400;font-style:normal}
          @font-face{font-family:"Vazirmatn";src:url("/fonts/Vazirmatn-Bold.woff2") format("woff2");font-weight:700;font-style:normal}
          *{box-sizing:border-box}
          html,body{width:210mm;height:148mm;margin:0;padding:0;overflow:hidden;background:#fff;color:#15242d}
          body{direction:rtl;font-family:"Vazirmatn",Tahoma,Arial,sans-serif}
          td,th,.payslip-total strong,.work-summary b{font-variant-numeric:tabular-nums;font-feature-settings:"tnum" 1}
          .payslip{width:209mm;height:147mm;margin:0;padding:3.5mm 5mm;overflow:hidden;break-inside:avoid;page-break-inside:avoid;page-break-after:avoid}
          .payslip-head{display:grid;grid-template-columns:1fr auto;align-items:center;gap:4mm;padding:0 0 2mm;border-bottom:.55mm solid #0b2d3b;font-size:9pt}
          .payslip-head div{display:grid;grid-template-columns:auto 1fr;align-items:center;gap:2mm 5mm}
          .payslip-head div b{font-size:10pt}.payslip-head div span{font-size:8.5pt}
          .payslip-head>strong{font-size:9.5pt}
          .payslip-person{display:grid;grid-template-columns:2fr 1fr 1fr 1fr;gap:1.5mm;padding:1.5mm 2mm;margin-top:1.4mm;background:#f7faf8;border:.2mm solid #dce6e0;border-radius:1.5mm;font-size:8pt}
          .payslip-person span{white-space:nowrap}
          .work-summary{display:grid;grid-template-columns:repeat(5,minmax(0,1fr));gap:1.2mm;padding:1.2mm;margin:1.3mm 0;background:#eef5f0;border-radius:1.5mm;font-size:7.5pt}
          .work-summary span{padding:.8mm 1mm;text-align:center;background:#fff;border:.2mm solid #dce6e0;border-radius:1mm}
          .work-summary b{display:block;margin-top:.5mm;font-size:8pt;color:#0b2d3b}
          .payslip-columns{display:grid;grid-template-columns:1fr 1fr;gap:2.5mm;align-items:start}
          table{width:100%;border-collapse:collapse;table-layout:fixed}
          th,td{padding:.5mm 1.1mm;border:.2mm solid #d8e1dc;font-size:7.3pt;line-height:1.15;text-align:right}
          th{background:#eef4f0}th:last-child,td:last-child{width:38%;white-space:nowrap}
          .payroll-warning{padding:.8mm 2mm;margin-bottom:1mm;border:.2mm solid #d59622;background:#fff7e4;font-size:7pt}
          .payslip-advance-summary{display:grid;grid-template-columns:repeat(3,1fr);gap:2mm;margin-top:1.2mm;padding:1mm 2mm;border:.2mm solid #cfded5;background:#f8fbf9;font-size:7.2pt}
          .payslip-total{display:grid;grid-template-columns:repeat(3,1fr);gap:2mm;margin-top:1.2mm;padding:1.2mm 2mm;background:#eaf3ec;border-radius:1.5mm;font-size:8pt}
          .payslip-total strong{font-size:9pt}
          .payslip-signatures{display:grid;grid-template-columns:1fr 1fr;gap:20mm;margin-top:1.5mm;padding:0 14mm;font-size:8pt;text-align:center}
          .payslip-signatures span{display:grid;grid-template-rows:auto 6mm;align-items:start}
          .payslip-signatures span::after{content:"";align-self:end;border-bottom:.25mm solid #68777f}
        </style></head><body><article class="payslip">${payslip.innerHTML}</article></body></html>`);
      printWindow.document.close();
      printWindow.focus();
      // پیش از چاپ منتظر بارگذاری فونت وزیرمتن می‌مانیم تا فیش با فونت درست چاپ شود
      const doPrint = () => {
        try { printWindow.print(); } finally { setTimeout(() => { if (!printWindow.closed) printWindow.close(); }, 250); }
      };
      const fonts = printWindow.document.fonts;
      if (fonts && fonts.ready) {
        Promise.race([fonts.ready, new Promise(r => setTimeout(r, 1500))]).then(() => setTimeout(doPrint, 120));
      } else {
        setTimeout(doPrint, 250);
      }
    },

    print: () => window.print(),

    todayLongFa: () => new Intl.DateTimeFormat("fa-IR", { dateStyle: "long" }).format(new Date()),

    downloadFile: (fileName, contentType, base64) => {
      const link = document.createElement("a");
      link.download = fileName;
      link.href = `data:${contentType};base64,${base64}`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
    },

    readFileAsBase64: async inputElement => {
      const file = inputElement?.files?.[0];
      if (!file) return null;
      const buffer = await file.arrayBuffer();
      let binary = "";
      const bytes = new Uint8Array(buffer);
      for (let i = 0; i < bytes.byteLength; i += 1) binary += String.fromCharCode(bytes[i]);
      return { name: file.name, type: file.type, base64: window.btoa(binary) };
    }
  };
})();
