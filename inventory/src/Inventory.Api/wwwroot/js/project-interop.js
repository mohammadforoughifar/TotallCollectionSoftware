// توابع تعامل جاوااسکریپت ماژول پروژه‌ها (چاپ گزارش / دانلود و نمایش پیوست‌ها)
// نکته: این کد عمداً در فایل جداگانه نگهداری می‌شود تا رشته‌های حاوی تگ‌های HTML
// (مانند </style> و </body>) بلاک inline script را در index.html نشکنند.
// چاپ گزارش به‌صورت جدول (خروجی اکسل/چاپ ماژول پروژه)
window.printTableReport = function (opt) {
    var esc = function (s) {
        return String(s == null ? '' : s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/\"/g, '&quot;').replace(/'/g, '&#39;');
    };
    var w = window.open('', '_blank', 'width=1020,height=760');
    if (!w) { alert('پنجره چاپ باز نشد — لطفاً مسدودکننده پاپ‌آپ مرورگر را غیرفعال کنید.'); return; }

    var meta = (opt.meta || []).map(function (m) { return '<span class="chip">' + esc(m) + '</span>'; }).join('');
    var head = (opt.headers || []).map(function (h) { return '<th>' + esc(h) + '</th>'; }).join('');
    var body = (opt.rows || []).map(function (r) {
        var tds = r.map(function (c, i) {
            return '<td class="' + (i === r.length - 1 ? 'desc' : 'num') + '">' + esc(c) + '</td>';
        }).join('');
        return '<tr>' + tds + '</tr>';
    }).join('');
    var foot = opt.footerTotal
        ? '<tfoot><tr><td colspan="' + ((opt.headers || []).length) + '">' + esc(opt.footerTotal) + '</td></tr></tfoot>'
        : '';

    var html =
        '<!DOCTYPE html><html lang="fa" dir="rtl"><head><meta charset="utf-8" />' +
        '<title>' + esc(opt.title) + '</title>' +
        '<style>' +
        '  * { box-sizing: border-box; }' +
        '  body { font-family: Tahoma, "Segoe UI", sans-serif; color: #1e293b; margin: 0; padding: 18px 22px; font-size: 11.5px; }' +
        '  .brand { display: flex; justify-content: space-between; align-items: baseline; border-bottom: 2.5px solid #4f46e5; padding-bottom: 8px; margin-bottom: 12px; }' +
        '  .brand .sys { font-size: 10px; color: #64748b; }' +
        '  h1 { font-size: 19px; margin: 0; color: #312e81; }' +
        '  .sub { font-size: 13px; color: #475569; margin: 2px 0 10px; font-weight: 700; }' +
        '  .chips { margin-bottom: 12px; line-height: 2.1; }' +
        '  .chip { display: inline-block; background: #eef2ff; border: 1px solid #c7d2fe; color: #3730a3; border-radius: 999px; padding: 1px 12px; margin-left: 6px; font-size: 10.5px; font-weight: 700; }' +
        '  table { width: 100%; border-collapse: collapse; }' +
        '  th { background: #312e81; color: #fff; font-weight: 700; padding: 6px 7px; border: 1px solid #312e81; font-size: 11px; white-space: nowrap; }' +
        '  td { border: 1px solid #cbd5e1; padding: 5px 7px; vertical-align: top; }' +
        '  td.num { text-align: center; white-space: nowrap; }' +
        '  td.desc { text-align: right; }' +
        '  tbody tr:nth-child(even) td { background: #f8fafc; }' +
        '  tfoot td { background: #fef3c7; border: 1px solid #d4a106; font-weight: 800; padding: 7px; font-size: 12px; color: #78350f; }' +
        '  .sign { margin-top: 34px; display: flex; justify-content: space-between; color: #475569; }' +
        '  .sign .line { display: inline-block; width: 170px; border-top: 1px dashed #94a3b8; padding-top: 4px; text-align: center; }' +
        '  .footer-meta { margin-top: 12px; font-size: 9.5px; color: #94a3b8; border-top: 1px solid #e2e8f0; padding-top: 6px; display: flex; justify-content: space-between; }' +
        '  @page { size: A4 portrait; margin: 10mm; }' +
        '  @media print { body { padding: 0; } }' +
        '</style></head><body>' +
        '<div class="brand"><h1>' + esc(opt.title) + '</h1><span class="sys">سامانه انبار و فروش — فروغ آریا</span></div>' +
        (opt.subTitle ? '<div class="sub">' + esc(opt.subTitle) + '</div>' : '') +
        (meta ? '<div class="chips">' + meta + '</div>' : '') +
        '<table><thead><tr>' + head + '</tr></thead><tbody>' + body + '</tbody>' + foot + '</table>' +
        '<div class="sign"><span>تحویل‌دهنده: <span class="line"></span></span><span>تأییدکننده: <span class="line"></span></span></div>' +
        '<div class="footer-meta"><span>تاریخ چاپ: ' + esc(opt.printedAt || '') + '</span><span>چاپ‌کننده: ' + esc(opt.printedBy || '') + '</span></div>' +
        '</body></html>';

    w.document.open();
    w.document.write(html);
    w.document.close();
    w.focus();
    setTimeout(function () { w.print(); }, 350);
    w.onafterprint = function () { w.close(); };
};
// دانلود فایل از داده Base64 — برای پیوست‌های رمزنگاری‌شده ماژول پروژه
window.downloadBlob = function (fileName, contentType, base64) {
    const bin = atob(base64);
    const len = bin.length;
    const bytes = new Uint8Array(len);
    for (let i = 0; i < len; i++) bytes[i] = bin.charCodeAt(i);
    const blob = new Blob([bytes], { type: contentType || 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName || 'file';
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(function () { URL.revokeObjectURL(url); }, 4000);
};
// باز کردن فایل (تصاویر و…) در تب جدید — از داده Base64
window.openBlob = function (contentType, base64) {
    const bin = atob(base64);
    const len = bin.length;
    const bytes = new Uint8Array(len);
    for (let i = 0; i < len; i++) bytes[i] = bin.charCodeAt(i);
    const blob = new Blob([bytes], { type: contentType || 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    window.open(url, '_blank');
    setTimeout(function () { URL.revokeObjectURL(url); }, 60000);
};
