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
