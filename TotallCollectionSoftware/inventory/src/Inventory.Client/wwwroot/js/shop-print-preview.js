// Shop-only, explicit print preview. The generic giPrint / office print helpers remain unchanged.
(function () {
    'use strict';

    let current = null;
    let sequence = 0;
    const assetRoot = document.currentScript && document.currentScript.src
        ? new URL('../', document.currentScript.src) : new URL('./', document.baseURI);
    const mmToPx = 96 / 25.4;

    function setStatus(session, message, error) {
        if (current !== session) return;
        session.status.textContent = message;
        session.status.classList.toggle('is-error', !!error);
    }

    function destroyPreviewPdf(session) {
        if (!session.pdfKey || !window.filePreview) return;
        Promise.resolve(window.filePreview.destroyPdf(session.pdfKey)).catch(function (error) {
            console.error('[shop-print-preview] PDF cleanup:', error);
        });
    }

    function close(session) {
        if (!session || current !== session) return;
        current = null;
        window.removeEventListener('keydown', session.keyHandler, true);
        window.removeEventListener('resize', session.resizeHandler);
        if (session.resizeTimer) clearTimeout(session.resizeTimer);
        if (session.pdfRenderTask) { try { session.pdfRenderTask.cancel(); } catch (_) { } }
        if (session.pdfPrintTask) Promise.resolve(session.pdfPrintTask.destroy()).catch(function () { });
        if (session.frame && session.frame.contentWindow) {
            try { session.frame.contentWindow.removeEventListener('keydown', session.keyHandler, true); } catch (_) { }
        }
        destroyPreviewPdf(session);
        if (session.pdfFrame) session.pdfFrame.remove();
        if (session.pdfFallbackFrame) session.pdfFallbackFrame.remove();
        if (session.overlay) session.overlay.remove();
        if (session.pdfPrintUrls) session.pdfPrintUrls.forEach(function (url) { URL.revokeObjectURL(url); });
        if (session.objectUrl) URL.revokeObjectURL(session.objectUrl);
        if (session.previousFocus && session.previousFocus.isConnected && typeof session.previousFocus.focus === 'function') {
            session.previousFocus.focus({ preventScroll: true });
        }
    }

    async function printNow(session) {
        if (current !== session || !session.ready || session.printing) return;
        session.printing = true;
        if (session.kind === 'pdf') updatePdfControls(session);
        else session.printButton.disabled = true;
        try {
            let frame = session.frame;
            if (session.kind === 'pdf') {
                // Prefer printing the original, vector PDF in browsers with a PDF viewer.
                // Without a working viewer, prepare an isolated printable copy using PDF.js.
                frame = session.pdfFrameLoaded && navigator.pdfViewerEnabled !== false
                    ? session.pdfFrame : await preparePdfFallback(session);
            }
            if (current !== session) return;
            const target = frame && frame.contentWindow;
            if (!target || typeof target.print !== 'function') throw new Error('print unavailable');
            target.focus();
            // Intentionally the only print call: it requires an explicit button/keyboard action.
            target.print();
            if (session.kind === 'pdf') setStatus(session, 'در صورت باز نشدن پنجرهٔ چاپ، دوباره «چاپ» را بزنید یا فایل PDF را دریافت کنید.');
        } catch (error) {
            if (current === session) {
                console.error('[shop-print-preview] print:', error);
                const message = session.kind !== 'pdf'
                    ? 'مرورگر پنجرهٔ چاپ را باز نکرد؛ دوباره تلاش کنید.'
                    : error && error.message === 'mixed PDF page sizes'
                        ? 'صفحه‌های PDF اندازهٔ یکسان ندارند؛ فایل را دریافت و از نمایشگر PDF چاپ کنید.'
                        : error && error.message === 'PDF print too large'
                            ? 'فایل برای چاپ در این مرورگر بزرگ است؛ PDF را دریافت و از نمایشگر PDF چاپ کنید.'
                            : 'آماده‌سازی یا باز کردن چاپ PDF انجام نشد؛ فایل را دریافت و از نمایشگر PDF چاپ کنید.';
                setStatus(session, message, true);
            }
        } finally {
            session.printing = false;
            if (current === session) {
                if (session.kind === 'pdf') updatePdfControls(session);
                else session.printButton.disabled = !session.ready;
            }
        }
    }

    function keyboard(session, event) {
        if (current !== session || event.isComposing) return;
        const modified = event.altKey || event.ctrlKey || event.metaKey || event.shiftKey;
        if (event.key === 'Escape' && !modified) {
            event.preventDefault(); event.stopImmediatePropagation();
            if (!event.repeat) close(session);
            return;
        }
        if ((event.key === 'F4' && !modified) || ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 'p')) {
            event.preventDefault(); event.stopImmediatePropagation();
            if (!event.repeat) printNow(session);
            return;
        }
        // A preview covers the invoice. Do not let its save/delete/navigation
        // shortcuts reach the form underneath, even if focus was lost.
        if (['F1', 'F2', 'F3', 'F4', 'F7', 'F8'].includes(event.key)) {
            event.stopImmediatePropagation();
            if (!modified) event.preventDefault();
            return;
        }
        if (event.key !== 'Tab' || event.altKey || event.ctrlKey || event.metaKey) return;
        if (event.view && event.view !== window) {
            event.preventDefault(); event.stopImmediatePropagation();
            (event.shiftKey ? session.closeButton : session.printButton.disabled ? session.closeButton : session.printButton).focus();
            return;
        }
        const candidates = Array.from(session.overlay.querySelectorAll('button:not(:disabled), a[href], iframe:not([tabindex="-1"])'))
            .filter(function (el) { return !el.hidden && el.getClientRects().length > 0; });
        if (!candidates.length) return;
        const active = document.activeElement;
        if (!session.overlay.contains(active) || (!event.shiftKey && active === candidates[candidates.length - 1])) {
            event.preventDefault(); candidates[0].focus();
        } else if (event.shiftKey && active === candidates[0]) {
            event.preventDefault(); candidates[candidates.length - 1].focus();
        }
    }

    function create(kind, title) {
        if (current || !document.body) return null;
        const overlay = document.createElement('div');
        overlay.className = 'shop-print-preview';
        overlay.setAttribute('role', 'dialog');
        overlay.setAttribute('aria-modal', 'true');
        overlay.setAttribute('aria-label', 'پیش‌نمایش چاپ');
        overlay.setAttribute('data-shop-dialog', '');
        overlay.tabIndex = -1;
        overlay.innerHTML = '<header class="shop-print-head">' +
            '<div class="shop-print-heading"><span class="shop-print-heading-icon" aria-hidden="true"><i class="bi bi-printer"></i></span>' +
            '<div class="shop-print-heading-text"><strong>پیش‌نمایش چاپ</strong><span class="shop-print-document-title"></span></div></div>' +
            '<div class="shop-print-tools" aria-label="اندازهٔ نمایش">' +
            '<button type="button" class="shop-print-tool-button" data-action="zoom-out" aria-label="کوچک‌نمایی" title="کوچک‌نمایی">−</button>' +
            '<span class="shop-print-zoom-label" aria-live="polite">۱۰۰٪</span>' +
            '<button type="button" class="shop-print-tool-button" data-action="zoom-in" aria-label="بزرگ‌نمایی" title="بزرگ‌نمایی">+</button></div>' +
            '<div class="shop-print-pages" hidden>' +
            '<button type="button" data-action="prev" aria-label="صفحهٔ قبل">قبلی</button>' +
            '<span class="shop-print-page-label" aria-live="polite"></span>' +
            '<button type="button" data-action="next" aria-label="صفحهٔ بعد">بعدی</button></div>' +
            '<div class="shop-print-actions"><a class="shop-print-download" hidden>دریافت PDF</a>' +
            '<button type="button" class="shop-print-primary" data-action="print" data-hotkey="f4" disabled><i class="bi bi-printer" aria-hidden="true"></i> چاپ (F4)</button>' +
            '<button type="button" data-action="close" data-hotkey="esc">بستن (Esc)</button></div></header>' +
            '<div class="shop-print-status" role="status" aria-live="polite">در حال آماده‌سازی پیش‌نمایش…</div>' +
            '<div class="shop-print-stage"><div class="shop-print-frame-wrap"><iframe class="shop-print-frame" title="نسخهٔ قابل چاپ" sandbox="allow-same-origin allow-modals"></iframe></div>' +
            '<div class="shop-print-pdf-host" aria-label="صفحهٔ PDF" hidden></div></div>';
        const session = {
            kind: kind, overlay: overlay, previousFocus: document.activeElement,
            title: overlay.querySelector('.shop-print-document-title'),
            status: overlay.querySelector('.shop-print-status'),
            printButton: overlay.querySelector('[data-action="print"]'),
            closeButton: overlay.querySelector('[data-action="close"]'),
            zoomOut: overlay.querySelector('[data-action="zoom-out"]'),
            zoomIn: overlay.querySelector('[data-action="zoom-in"]'),
            zoomLabel: overlay.querySelector('.shop-print-zoom-label'),
            pages: overlay.querySelector('.shop-print-pages'),
            pageLabel: overlay.querySelector('.shop-print-page-label'),
            prevButton: overlay.querySelector('[data-action="prev"]'),
            nextButton: overlay.querySelector('[data-action="next"]'),
            download: overlay.querySelector('.shop-print-download'),
            stage: overlay.querySelector('.shop-print-stage'),
            frameWrap: overlay.querySelector('.shop-print-frame-wrap'),
            frame: overlay.querySelector('.shop-print-frame'),
            pdfHost: overlay.querySelector('.shop-print-pdf-host'),
            width: 210 * mmToPx, zoom: 1, page: 1, pageCount: 0, ready: false, printing: false
        };
        session.title.textContent = title || 'نسخهٔ قابل چاپ';
        current = session;
        document.body.appendChild(overlay);
        session.keyHandler = function (event) { keyboard(session, event); };
        session.resizeHandler = function () {
            if (current !== session) return;
            if (session.kind !== 'pdf') layout(session);
            else if (session.pdfLoaded) {
                clearTimeout(session.resizeTimer);
                session.resizeTimer = setTimeout(function () { renderPdf(session); }, 200);
            }
        };
        window.addEventListener('keydown', session.keyHandler, true);
        window.addEventListener('resize', session.resizeHandler);
        session.closeButton.addEventListener('click', function () { close(session); });
        session.printButton.addEventListener('click', function () { printNow(session); });
        session.zoomOut.addEventListener('click', function () { zoom(session, -.2); });
        session.zoomIn.addEventListener('click', function () { zoom(session, .2); });
        session.prevButton.addEventListener('click', function () {
            if (session.page > 1 && !session.pdfRendering) { session.page--; renderPdf(session); }
        });
        session.nextButton.addEventListener('click', function () {
            if (session.page < session.pageCount && !session.pdfRendering) { session.page++; renderPdf(session); }
        });
        session.closeButton.focus({ preventScroll: true });
        return session;
    }

    function layout(session) {
        if (current !== session || session.kind === 'pdf') return;
        const style = getComputedStyle(session.stage);
        const horizontal = parseFloat(style.paddingLeft || 0) + parseFloat(style.paddingRight || 0);
        const vertical = parseFloat(style.paddingTop || 0) + parseFloat(style.paddingBottom || 0);
        const availableWidth = Math.max(1, session.stage.clientWidth - horizontal);
        const availableHeight = Math.max(120, session.stage.clientHeight - vertical);
        const fit = Math.min(1, availableWidth / session.width);
        const scale = Math.max(.1, fit * session.zoom);
        session.frameWrap.style.width = (session.width * scale) + 'px';
        session.frameWrap.style.height = availableHeight + 'px';
        session.frame.style.width = session.width + 'px';
        session.frame.style.height = (availableHeight / scale) + 'px';
        session.frame.style.transform = 'scale(' + scale + ')';
        session.zoomLabel.textContent = Math.round(scale * 100).toLocaleString('fa-IR') + '٪';
    }

    function zoom(session, delta) {
        if (current !== session || (session.kind === 'pdf' && !session.pdfLoaded)) return;
        session.zoom = Math.max(.4, Math.min(2.6, Math.round((session.zoom + delta) * 10) / 10));
        if (session.kind === 'pdf') renderPdf(session); else layout(session);
    }

    function withTimeout(promise, ms) {
        let timer;
        return Promise.race([promise, new Promise(function (_, reject) {
            timer = setTimeout(function () { reject(new Error('preview timeout')); }, ms);
        })]).finally(function () { clearTimeout(timer); });
    }

    function imageReady(image) {
        if (!image.getAttribute('src') && !image.getAttribute('srcset')) return Promise.resolve();
        if (image.complete) return image.naturalWidth ? Promise.resolve() : Promise.reject(new Error('image load failed'));
        return new Promise(function (resolve, reject) {
            image.addEventListener('load', resolve, { once: true });
            image.addEventListener('error', function () { reject(new Error('image load failed')); }, { once: true });
        });
    }

    // Apply the existing @media print declarations to the screen preview only.
    // The real print still uses the unmodified original stylesheets.
    function mirrorPrintRules(doc, margin) {
        const result = [];
        function visit(rules) {
            for (const rule of Array.from(rules || [])) {
                if (rule.media && rule.cssRules) {
                    const media = rule.media.mediaText || '';
                    if (/\bprint\b/i.test(media) && !/\bnot\s+print\b/i.test(media)) {
                        result.push(rule.cssText.replace(/^@media\s*([^\{]*)/i, function (_, condition) {
                            return '@media ' + condition.replace(/\bprint\b/gi, 'screen');
                        }));
                    }
                } else if (rule.styleSheet) {
                    try { visit(rule.styleSheet.cssRules); } catch (_) { }
                } else if (rule.cssRules) visit(rule.cssRules);
            }
        }
        for (const sheet of Array.from(doc.styleSheets)) {
            try {
                if (sheet.media && /\bprint\b/i.test(sheet.media.mediaText || '')) {
                    result.push('@media screen {' + Array.from(sheet.cssRules).map(function (r) { return r.cssText; }).join('\n') + '}');
                } else visit(sheet.cssRules);
            } catch (error) { console.warn('[shop-print-preview] stylesheet cannot be read:', error); }
        }
        const style = doc.createElement('style');
        style.setAttribute('data-shop-print-screen', '');
        style.textContent = result.join('\n') + '\n@media screen { html { background:#fff; } body { box-sizing:border-box; margin:0; padding:' + margin + '; min-height:100vh; } }';
        doc.head.appendChild(style);
    }

    async function prepareHtml(session, styleLoads, margin) {
        try {
            await withTimeout(Promise.all(styleLoads), 15000);
            if (current !== session) return;
            const doc = session.frame.contentDocument;
            if (!doc || !doc.body) throw new Error('preview document unavailable');
            mirrorPrintRules(doc, margin);
            // Force layout so used @font-face resources enter document.fonts.ready.
            doc.documentElement.getBoundingClientRect();
            const tasks = Array.from(doc.images).map(imageReady);
            if (doc.fonts && doc.fonts.ready) tasks.push(doc.fonts.ready);
            await withTimeout(Promise.all(tasks), 15000);
            if (current !== session) return;
            session.ready = true;
            session.printButton.disabled = false;
            setStatus(session, 'پیش‌نمایش آماده است. برای ادامه «چاپ» را بزنید؛ صفحه‌بندی نهایی در تنظیمات چاپ مرورگر مشخص می‌شود.');
        } catch (error) {
            console.error('[shop-print-preview] preparation:', error);
            setStatus(session, 'پیش‌نمایش کامل بارگذاری نشد؛ اتصال و فایل‌های برنامه را بررسی کرده و دوباره تلاش کنید.', true);
        }
    }

    function activateFrame(session) {
        const win = session.frame.contentWindow;
        if (!win || !win.document) throw new Error('preview frame unavailable');
        win.addEventListener('keydown', session.keyHandler, true);
        return win.document;
    }

    function pageSettings(html) {
        const block = /@page\s*\{([^}]*)\}/i.exec(html || '');
        const css = block ? block[1] : '';
        const size = /\bsize\s*:\s*(A[45])\b/i.exec(css);
        const marginMatch = /\bmargin\s*:\s*([^;}]*)/i.exec(css);
        const margin = marginMatch && /^(?:\s*\d+(?:\.\d+)?(?:mm|cm|in|pt|px)\s*){1,4}$/i.test(marginMatch[1])
            ? marginMatch[1].trim() : '10mm';
        return { width: (size && size[1].toUpperCase() === 'A5' ? 148 : 210) * mmToPx, margin: margin };
    }

    function openHtml(html) {
        if (typeof html !== 'string' || !html.trim()) return false;
        let session;
        try {
            session = create('html', 'سند');
            if (!session) return false;
            const settings = pageSettings(html);
            session.width = settings.width;
            const doc = activateFrame(session);
            const base = new URL('./', document.baseURI).href;
            doc.open();
            doc.write(html.split('__FONT_BASE__').join(base));
            doc.close();
            if (doc.title) session.title.textContent = doc.title;
            // document.open/write may replace listeners in some browsers.
            session.frame.contentWindow.removeEventListener('keydown', session.keyHandler, true);
            session.frame.contentWindow.addEventListener('keydown', session.keyHandler, true);
            layout(session);
            prepareHtml(session, [], settings.margin);
            return true;
        } catch (error) {
            console.error('[shop-print-preview] open HTML:', error);
            close(session);
            return false;
        }
    }

    function snapshotElement(source, doc, mode) {
        const clone = source.cloneNode(true);
        const originalControls = source.querySelectorAll('input, textarea, select');
        const copiedControls = clone.querySelectorAll('input, textarea, select');
        originalControls.forEach(function (original, index) {
            const copy = copiedControls[index];
            if (!copy) return;
            if (original.tagName === 'SELECT') {
                Array.from(original.options).forEach(function (option, i) { if (copy.options[i]) copy.options[i].selected = option.selected; });
            } else if (original.type === 'checkbox' || original.type === 'radio') copy.checked = original.checked;
            else copy.value = original.value;
        });
        const originals = source.querySelectorAll('canvas');
        const copies = clone.querySelectorAll('canvas');
        originals.forEach(function (canvas, index) {
            const copy = copies[index];
            if (!copy) return;
            try {
                const img = doc.createElement('img');
                for (const attr of Array.from(copy.attributes)) img.setAttribute(attr.name, attr.value);
                img.src = canvas.toDataURL('image/png');
                img.alt = canvas.getAttribute('aria-label') || '';
                copy.replaceWith(img);
            } catch (error) {
                // A blank QR/chart would be a misleading and potentially unusable print.
                throw new Error('canvas snapshot failed: ' + (error && error.message || 'unknown error'));
            }
        });
        clone.querySelectorAll('script, iframe, object, embed, style').forEach(function (node) { node.remove(); });
        // A guide prints every FAQ, without changing the open/closed state on the original page.
        if (mode === 'guide') clone.querySelectorAll('details').forEach(function (details) { details.open = true; });
        clone.setAttribute('inert', '');
        return clone;
    }

    function copyStyles(doc) {
        const loads = [];
        for (const node of Array.from(document.querySelectorAll('link[rel~="stylesheet"], style'))) {
            if (node.closest('.shop-print-preview')) continue;
            // The global office stylesheet hides every non-letter element in print.
            // Exclude it only from this isolated shop snapshot; never change its
            // rules or its loading/printing behavior in the main application.
            if (node.tagName === 'LINK' && /\/css\/letters\.css$/i.test(new URL(node.href, document.baseURI).pathname)) continue;
            const copy = doc.importNode(node, true);
            if (node.tagName === 'LINK') {
                copy.href = node.href;
                loads.push(new Promise(function (resolve, reject) {
                    copy.addEventListener('load', resolve, { once: true });
                    copy.addEventListener('error', function () { reject(new Error('stylesheet load failed')); }, { once: true });
                }));
            }
            doc.head.appendChild(copy);
        }
        return loads;
    }

    function openElement(element, title, mode) {
        if (!element || !element.isConnected) return false;
        let session;
        try {
            session = create('element', title);
            if (!session) return false;
            const doc = activateFrame(session);
            doc.open();
            doc.write('<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"></head><body></body></html>');
            doc.close();
            const base = doc.createElement('base'); base.href = document.baseURI; doc.head.appendChild(base);
            const snapshot = snapshotElement(element, doc, mode);
            const loads = copyStyles(doc);
            if (mode === 'card') doc.body.classList.add('print-card-mode');
            doc.body.appendChild(snapshot);
            session.frame.contentWindow.removeEventListener('keydown', session.keyHandler, true);
            session.frame.contentWindow.addEventListener('keydown', session.keyHandler, true);
            layout(session);
            prepareHtml(session, loads, '10mm');
            return true;
        } catch (error) {
            console.error('[shop-print-preview] open element:', error);
            close(session);
            return false;
        }
    }

    // Fallback for browsers without an embedded PDF printer. Render each original
    // PDF page once at print resolution into an isolated document. It is built
    // only after the user requests print and is reused for subsequent attempts.
    async function preparePdfFallback(session) {
        if (session.pdfFallbackReady && session.pdfFallbackFrame) return session.pdfFallbackFrame;
        const frame = document.createElement('iframe');
        frame.className = 'shop-print-pdf-frame shop-print-pdf-render-frame';
        frame.title = 'نسخهٔ چاپ PDF';
        frame.tabIndex = -1;
        frame.setAttribute('aria-hidden', 'true');
        frame.setAttribute('sandbox', 'allow-same-origin allow-modals');
        document.body.appendChild(frame);
        session.pdfFallbackFrame = frame;
        session.pdfPrintUrls = [];
        let task;
        try {
            setStatus(session, 'در حال آماده‌سازی صفحات PDF برای چاپ…');
            const doc = frame.contentDocument;
            if (!doc) throw new Error('PDF print frame unavailable');
            doc.open();
            doc.write('<!doctype html><html><head><meta charset="utf-8"></head><body></body></html>');
            doc.close();
            const lib = await import(new URL('lib/pdfjs/pdf.js?v=6.3.289', assetRoot).href);
            if (current !== session) throw new Error('preview closed');
            lib.GlobalWorkerOptions.workerSrc = new URL('lib/pdfjs/pdf.worker.js?v=6.3.289', assetRoot).href;
            task = lib.getDocument({ url: session.objectUrl,
                cMapUrl: new URL('lib/pdfjs/cmaps/', assetRoot).href, cMapPacked: true,
                standardFontDataUrl: new URL('lib/pdfjs/standard_fonts/', assetRoot).href,
                wasmUrl: new URL('lib/pdfjs/wasm/', assetRoot).href,
                isEvalSupported: false, enableXfa: false });
            session.pdfPrintTask = task;
            const pdf = await task.promise;
            let firstSize, printScale;
            for (let number = 1; number <= pdf.numPages; number++) {
                if (current !== session) throw new Error('preview closed');
                setStatus(session, 'در حال آماده‌سازی صفحهٔ ' + number.toLocaleString('fa-IR') + ' از ' + pdf.numPages.toLocaleString('fa-IR') + ' برای چاپ…');
                const page = await pdf.getPage(number);
                const unit = page.getViewport({ scale: 1 });
                if (!firstSize) {
                    firstSize = { width: unit.width, height: unit.height };
                    // Decoded page images accumulate until the browser prints them.
                    // Bound the total bitmap memory and keep at least 150 DPI;
                    // a larger job should use the downloadable vector original.
                    const pixelBudget = navigator.deviceMemory && navigator.deviceMemory <= 2 ? 48000000 : 96000000;
                    const pixelsPerUnitPage = unit.width * unit.height;
                    printScale = Math.min(200 / 72, 8192 / Math.max(unit.width, unit.height),
                        Math.sqrt(16000000 / pixelsPerUnitPage), Math.sqrt(pixelBudget / (pdf.numPages * pixelsPerUnitPage)));
                    if (!Number.isFinite(printScale) || printScale < 150 / 72) throw new Error('PDF print too large');
                    const style = doc.createElement('style');
                    style.textContent = '@page{size:' + unit.width.toFixed(3) + 'pt ' + unit.height.toFixed(3) + 'pt;margin:0}' +
                        'html,body{margin:0;padding:0;background:white}.page{width:' + unit.width.toFixed(3) + 'pt;height:' + unit.height.toFixed(3) + 'pt;' +
                        'overflow:hidden;break-after:page;page-break-after:always;print-color-adjust:exact;-webkit-print-color-adjust:exact}' +
                        '.page:last-child{break-after:auto;page-break-after:auto}.page img{display:block;width:100%;height:100%}';
                    doc.head.appendChild(style);
                } else if (Math.abs(unit.width - firstSize.width) > 1 || Math.abs(unit.height - firstSize.height) > 1) {
                    // Never silently crop or rotate a mixed-size PDF.
                    throw new Error('mixed PDF page sizes');
                }
                const viewport = page.getViewport({ scale: printScale });
                const canvas = doc.createElement('canvas');
                canvas.width = Math.max(1, Math.ceil(viewport.width));
                canvas.height = Math.max(1, Math.ceil(viewport.height));
                const context = canvas.getContext('2d', { alpha: false });
                if (!context) throw new Error('PDF print canvas unavailable');
                session.pdfRenderTask = page.render({ canvasContext: context, viewport: viewport, intent: 'print' });
                await session.pdfRenderTask.promise;
                session.pdfRenderTask = null;
                const blob = await new Promise(function (resolve) { canvas.toBlob(resolve, 'image/png'); });
                canvas.width = 1; canvas.height = 1;
                if (current !== session) throw new Error('preview closed');
                if (!blob) throw new Error('PDF print image unavailable');
                const url = URL.createObjectURL(blob);
                session.pdfPrintUrls.push(url);
                const section = doc.createElement('div'); section.className = 'page';
                const image = doc.createElement('img'); image.alt = ''; image.src = url;
                section.appendChild(image); doc.body.appendChild(section);
                if (image.decode) await image.decode();
                page.cleanup();
            }
            if (current !== session || !pdf.numPages) throw new Error('preview closed or empty PDF');
            session.pdfFallbackReady = true;
            return frame;
        } catch (error) {
            if (session.pdfFallbackFrame) session.pdfFallbackFrame.remove();
            session.pdfFallbackFrame = null;
            session.pdfPrintUrls.forEach(function (url) { URL.revokeObjectURL(url); });
            session.pdfPrintUrls = [];
            throw error;
        } finally {
            session.pdfRenderTask = null;
            if (task) { try { await task.destroy(); } catch (_) { } }
            if (session.pdfPrintTask === task) session.pdfPrintTask = null;
        }
    }

    function pdfBytes(payload) {
        if (payload instanceof Uint8Array) return payload;
        if (payload instanceof ArrayBuffer) return new Uint8Array(payload);
        if (Array.isArray(payload)) return new Uint8Array(payload);
        const binary = atob(String(payload || ''));
        return Uint8Array.from(binary, function (c) { return c.charCodeAt(0); });
    }

    function updatePdfControls(session) {
        session.pageLabel.textContent = session.page.toLocaleString('fa-IR') + ' / ' + session.pageCount.toLocaleString('fa-IR');
        session.zoomLabel.textContent = Math.round(session.zoom * 100).toLocaleString('fa-IR') + '٪';
        session.prevButton.disabled = !session.pdfLoaded || session.pdfRendering || session.printing || session.page <= 1;
        session.nextButton.disabled = !session.pdfLoaded || session.pdfRendering || session.printing || session.page >= session.pageCount;
        session.zoomOut.disabled = !session.pdfLoaded || session.pdfRendering || session.printing || session.zoom <= .4;
        session.zoomIn.disabled = !session.pdfLoaded || session.pdfRendering || session.printing || session.zoom >= 2.6;
        session.ready = !!session.pdfLoaded && !!session.pdfPageValid && !session.pdfRendering && !session.printing;
        session.printButton.disabled = !session.ready;
    }

    async function renderPdf(session) {
        if (current !== session || !session.pdfLoaded || !window.filePreview || session.printing) return;
        const generation = (session.pdfPreviewGeneration || 0) + 1;
        session.pdfPreviewGeneration = generation;
        session.pdfPageValid = false;
        session.pdfRendering = true; updatePdfControls(session);
        try {
            const result = await window.filePreview.renderPdf(session.pdfKey, session.page, session.zoom);
            if (current !== session || session.pdfPreviewGeneration !== generation) return;
            if (result.success) {
                session.pdfPageValid = true;
                setStatus(session, 'پیش‌نمایش آماده است. برای ادامه «چاپ» را بزنید.');
            } else if (result.code !== 'CANCELLED') {
                setStatus(session, result.message || 'نمایش صفحهٔ PDF انجام نشد.', true);
            }
        } catch (error) {
            console.error('[shop-print-preview] PDF page:', error);
            setStatus(session, 'نمایش صفحهٔ PDF انجام نشد.', true);
        } finally {
            if (current === session && session.pdfPreviewGeneration === generation) {
                session.pdfRendering = false;
                updatePdfControls(session);
            }
        }
    }

    function openPdf(payload, fileName) {
        let session;
        try {
            if (!window.filePreview || !window.filePreview.openPdf) throw new Error('PDF viewer unavailable');
            const bytes = pdfBytes(payload);
            const prefix = new TextDecoder('latin1').decode(bytes.subarray(0, Math.min(1024, bytes.length)));
            if (!bytes.length || !prefix.includes('%PDF-')) throw new Error('invalid PDF');
            session = create('pdf', 'فهرست کالا');
            if (!session) return false;
            session.pdfKey = 'shop-print-' + (++sequence);
            session.objectUrl = URL.createObjectURL(new Blob([bytes], { type: 'application/pdf' }));
            session.frameWrap.hidden = true;
            session.pdfHost.hidden = false;
            session.pages.hidden = false;
            session.download.hidden = false;
            session.download.href = session.objectUrl;
            session.download.download = fileName || 'report.pdf';
            session.pdfFrame = document.createElement('iframe');
            session.pdfFrame.className = 'shop-print-pdf-frame';
            session.pdfFrame.title = 'آماده‌سازی چاپ PDF';
            session.pdfFrame.tabIndex = -1;
            session.pdfFrame.setAttribute('aria-hidden', 'true');
            session.pdfFrame.addEventListener('load', function () {
                if (current !== session) return;
                session.pdfFrameLoaded = true;
                updatePdfControls(session);
            });
            session.pdfFrame.src = session.objectUrl;
            document.body.appendChild(session.pdfFrame);
            updatePdfControls(session);
            Promise.resolve(window.filePreview.openPdf(session.pdfHost, session.objectUrl, null, session.pdfKey))
                .then(function (result) {
                    // openPdf may create its viewer after close() has already run.
                    if (current !== session) { destroyPreviewPdf(session); return; }
                    if (!result.success || !result.pages) { setStatus(session, result.message || 'نمایش PDF ممکن نشد.', true); return; }
                    session.pageCount = result.pages;
                    session.pdfLoaded = true;
                    session.pdfPageValid = true;
                    updatePdfControls(session);
                    if (session.ready) setStatus(session, 'پیش‌نمایش آماده است. برای ادامه «چاپ» را بزنید.');
                }).catch(function (error) {
                    if (current !== session) { destroyPreviewPdf(session); return; }
                    console.error('[shop-print-preview] PDF:', error);
                    setStatus(session, 'نمایش PDF ممکن نشد؛ می‌توانید فایل را دریافت کنید.', true);
                });
            return true;
        } catch (error) {
            console.error('[shop-print-preview] open PDF:', error);
            close(session);
            return false;
        }
    }

    // Delegated only to shop print-setting bank fields; no other form is affected.
    function insertBankDigits(event, text) {
        const input = event.target;
        if (!(input instanceof HTMLInputElement) || !input.hasAttribute('data-shop-bank')) return;
        event.preventDefault();
        const max = Number(input.dataset.shopBank);
        let normalized = String(text || '').replace(/[۰-۹]/g, c => String(c.charCodeAt(0) - 1776))
            .replace(/[٠-٩]/g, c => String(c.charCodeAt(0) - 1632)).replace(/[\s-]/g, '').toUpperCase();
        if (max === 24) normalized = normalized.replace(/^IR/, '');
        const start = input.selectionStart ?? input.value.length, end = input.selectionEnd ?? start;
        if (!/^\d*$/.test(normalized) || input.value.length - (end - start) + normalized.length > max) return;
        input.setRangeText(normalized, start, end, 'end');
        input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    document.addEventListener('beforeinput', event => {
        if (event.inputType === 'insertText' && event.data) insertBankDigits(event, event.data);
    });
    document.addEventListener('paste', event => insertBankDigits(event, event.clipboardData?.getData('text') || ''));

    let paperChoice = null;
    function choosePaper() {
        if (paperChoice) return paperChoice;
        const previousFocus = document.activeElement;
        paperChoice = new Promise(function (resolve) {
            const dialog = document.createElement('dialog');
            dialog.className = 'shop-paper-choice';
            dialog.dir = 'rtl';
            dialog.setAttribute('aria-labelledby', 'shop-paper-title');
            dialog.innerHTML = '<form method="dialog"><h2 id="shop-paper-title">اندازهٔ کاغذ این چاپ</h2>' +
                '<label for="shop-paper-select">کاغذ<select id="shop-paper-select" autofocus><option value="A4">A4</option><option value="A5">A5</option></select></label>' +
                '<div><button value="cancel">انصراف</button><button value="confirm" class="confirm">ادامهٔ چاپ</button></div></form>';
            dialog.addEventListener('close', function () {
                const value = dialog.returnValue === 'confirm' ? dialog.querySelector('select').value : null;
                dialog.remove();
                paperChoice = null;
                if (previousFocus && previousFocus.isConnected) previousFocus.focus();
                resolve(value);
            }, { once: true });
            document.body.appendChild(dialog);
            dialog.showModal();
        });
        return paperChoice;
    }

    window.shopPrintPreview = { choosePaper: choosePaper, openHtml: openHtml, openElement: openElement, openPdf: openPdf };
})();
