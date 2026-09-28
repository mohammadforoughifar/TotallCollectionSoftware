// ابزار مشترک منوهای شناور (تقویم / کمبوباکس)
// مختصات المان را در مختصات viewport برمی‌گرداند تا منو با position:fixed
// دقیقا زیر (یا بالای) المان قرار بگیرد و دیگر زیر کارت‌ها نرود یا بریده نشود.
window.uiRect = function (el) {
    if (!el) {
        return { left: 8, top: 8, right: 238, bottom: 46, width: 230, height: 38, vw: window.innerWidth, vh: window.innerHeight };
    }
    var r = el.getBoundingClientRect();
    return {
        left: r.left,
        top: r.top,
        right: r.right,
        bottom: r.bottom,
        width: r.width,
        height: r.height,
        vw: window.innerWidth,
        vh: window.innerHeight
    };
};

// اسکرول نرم یا فوری گزینه هایلایت‌شده در منوهای کمبوباکس (SearchSelect)
window.scrollIntoViewById = function (id) {
    var el = document.getElementById(id);
    if (el && typeof el.scrollIntoView === 'function') {
        el.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    }
};

// جداسازی ۳ رقم ۳ رقم لحظه‌ای اعداد (قیمت واحد / فی) هنگام تایپ کاربر
(function () {
    var persianMap = { '۰': '0', '۱': '1', '۲': '2', '۳': '3', '۴': '4', '۵': '5', '۶': '6', '۷': '7', '۸': '8', '۹': '9' };
    var arabicMap  = { '٠': '0', '١': '1', '٢': '2', '٣': '3', '٤': '4', '٥': '5', '٦': '6', '٧': '7', '٨': '8', '٩': '9' };

    function toEnglishDigits(str) {
        return (str || '').replace(/[۰-۹]/g, function (d) { return persianMap[d] || d; })
                          .replace(/[٠-٩]/g, function (d) { return arabicMap[d] || d; });
    }

    // مدیریت Backspace اگر مکان‌نما دقیقاً بعد از کاراکتر کاما باشد
    document.addEventListener('keydown', function (e) {
        var target = e.target;
        if (!target || !target.matches || !target.matches('input[data-format="price"], input.price-input')) return;

        if (e.key === 'Backspace' && target.selectionStart === target.selectionEnd && target.selectionStart > 0) {
            var val = target.value;
            var pos = target.selectionStart;
            if (val[pos - 1] === ',') {
                e.preventDefault();
                // کاراکتر قبل از کاما را حذف کن
                var nextVal = val.slice(0, pos - 2) + val.slice(pos - 1);
                target.value = nextVal;
                // مکان‌نما یک موقعیت قبل از رقم حذف‌شده
                var newPos = Math.max(0, pos - 2);
                target.setSelectionRange(newPos, newPos);
                target.dispatchEvent(new Event('input', { bubbles: true }));
            }
        }
    }, true);

    // فرمت لحظه‌ای ۳ رقمی حین ورود متن در فیلدهای قیمت/فی
    document.addEventListener('input', function (e) {
        var target = e.target;
        if (!target || !target.matches || !target.matches('input[data-format="price"], input.price-input')) return;

        var raw = target.value;
        if (!raw) return;

        var converted = toEnglishDigits(raw);
        var curPos = target.selectionStart || 0;

        // شمارش ارقام و ممیز قبل از مکان‌نما تا موقعیت نسبی بعد از درج کاماها حفظ شود
        var rawBefore = converted.slice(0, curPos).replace(/[^0-9.]/g, '');
        var digitsBeforeCount = rawBefore.length;

        var clean = converted.replace(/[^0-9.]/g, '');
        if (!clean) {
            target.value = '';
            return;
        }

        var parts = clean.split('.');
        var intPart = parts[0] || '';
        var decPart = parts.length > 1 ? '.' + parts.slice(1).join('') : '';

        // جدا کردن سه رقم سه رقم بخش صحیح
        var formattedInt = intPart.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
        var formatted = (formattedInt || (parts.length > 1 ? '0' : '')) + decPart;

        target.value = formatted;

        // برگرداندن مکان‌نما به موقعیت صحیح متناظر
        var newCursor = 0;
        var foundDigits = 0;
        for (var i = 0; i < formatted.length; i++) {
            if (/[\d.]/.test(formatted[i])) {
                foundDigits++;
            }
            if (foundDigits >= digitsBeforeCount) {
                newCursor = i + 1;
                break;
            }
        }
        if (foundDigits < digitsBeforeCount) {
            newCursor = formatted.length;
        }

        try {
            target.setSelectionRange(newCursor, newCursor);
        } catch (_) { }
    }, true);
})();
