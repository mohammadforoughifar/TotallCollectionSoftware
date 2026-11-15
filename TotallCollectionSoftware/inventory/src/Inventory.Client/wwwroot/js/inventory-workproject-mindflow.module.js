// پوستهٔ ماژولیِ نگار ذهنی — فقط برای بارگذاری پویا (import) از کامپوننت.
//
// منطق در فایل کلاسیک inventory-workproject-mindflow.js است تا نسخهٔ کش‌شدهٔ index.html
// (که تگ script تازه را ندارد) هم بدون Ctrl+F5 کار کند. اینجا فقط همان توابع
// روی window صادر می‌شوند.
//
// از top-level await استفاده نمی‌کنیم تا روی مرورگرهای قدیمی‌تر هم بارگذاری شود؛
// بارگذاریِ تنبل داخل خودِ توابع انجام می‌شود و Blazor چون Promise را await می‌کند مشکلی ندارد.

async function api() {
    if (!(window.inventoryWorkProjectMindFlow &&
          typeof window.inventoryWorkProjectMindFlow.attachDrag === 'function')) {
        await import('./inventory-workproject-mindflow.js');
    }
    return window.inventoryWorkProjectMindFlow;
}

export const attachDrag = async (svgId, dotnet) => (await api()).attachDrag(svgId, dotnet);

export const detachDrag = async svgId => (await api()).detachDrag(svgId);

export const focusNodeTitle = async svgId => (await api()).focusNodeTitle(svgId);

export const getCanvasView = async wrap => (await api()).getCanvasView(wrap);

export const setCanvasView = async (wrap, left, top) => (await api()).setCanvasView(wrap, left, top);

export const bindCanvasKeys = async wrap => (await api()).bindCanvasKeys(wrap);

export const mountPrintPreview = async (srcSvgId, targetId) => (await api()).mountPrintPreview(srcSvgId, targetId);

export const focusPrintOverlay = async overlayId => (await api()).focusPrintOverlay(overlayId);

export const printPreview = async (targetId, title, subtitle, meta) => (await api()).printPreview(targetId, title, subtitle, meta);
