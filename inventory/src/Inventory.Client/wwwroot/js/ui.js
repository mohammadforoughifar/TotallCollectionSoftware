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

// اسکرول یک المان با شناسه؛ حالت پیش‌فرض برای گزینه هایلایت‌شده در کمبوباکس‌ها
// است و صفحات راهنما می‌توانند block و behavior دلخواه (مثلاً start/smooth) بفرستند.
window.scrollIntoViewById = function (id, block, behavior) {
    var el = document.getElementById(id);
    if (el && typeof el.scrollIntoView === 'function') {
        el.scrollIntoView({
            block: block || 'nearest',
            inline: 'nearest',
            behavior: behavior || 'auto'
        });
    }
};

// فوکوس روی اینپوت کالای آخرین سطر فرم سند انبار
window.focusLastProductInput = function () {
    setTimeout(function () {
        var inputs = document.querySelectorAll('.doc-grid tbody tr:last-child .ss input');
        if (inputs && inputs.length) {
            inputs[0].focus();
            if (inputs[0].select) inputs[0].select();
        }
    }, 60);
};

// Print the whole guide, including collapsed FAQ answers, without changing the
// reader's open/closed choices when the print dialog is dismissed.
window.officeGuides = {
    // Workspace tabs remain mounted when hidden. Search only this guide,
    // not a same-id section in another tab's copy of the guide.
    scrollTo: function (root, id) {
        const section = root && Array.from(root.querySelectorAll('section[id]'))
            .find(element => element.id === id);
        if (section) section.scrollIntoView({ block: 'start', inline: 'nearest',
            behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth' });
    },
    print: function (root) {
        if (!root) return;
        const collapsed = Array.from(root.querySelectorAll('details:not([open])'));
        const restore = function () {
            collapsed.forEach(detail => detail.open = false);
            window.removeEventListener('afterprint', restore);
        };
        collapsed.forEach(detail => detail.open = true);
        window.addEventListener('afterprint', restore, { once: true });
        try {
            window.print();
        } catch (error) {
            restore();
            throw error;
        }
    }
};
