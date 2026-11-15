// ─────────────────────────────────────────────────────────────────────────────
// «چشم» روی همهٔ فیلدهای رمز: با یک کلیک رمز دیده می‌شود و دوباره پنهان می‌شود.
// بدون کتابخانهٔ خارجی؛ همهٔ input[type=password] صفحه — حتی فیلدهایی که بعداً
// با جاوااسکریپت ساخته می‌شوند — خودکار پوشش داده می‌شوند.
// ─────────────────────────────────────────────────────────────────────────────
(function () {
  "use strict";

  var EYE = '<svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true" focusable="false">' +
    '<path fill="currentColor" d="M12 5c-5 0-9.3 3.1-11 7 1.7 3.9 6 7 11 7s9.3-3.1 11-7c-1.7-3.9-6-7-11-7zm0 12a5 5 0 1 1 0-10 5 5 0 0 1 0 10z"/>' +
    '<circle fill="currentColor" cx="12" cy="12" r="2.6"/></svg>';
  var EYE_OFF = '<svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true" focusable="false">' +
    '<path fill="currentColor" d="M2.4 3.8 3.8 2.4l17.8 17.8-1.4 1.4-3.1-3.1A12.6 12.6 0 0 1 12 19c-5 0-9.3-3.1-11-7a13.6 13.6 0 0 1 4.2-5.1L2.4 3.8zM12 7a5 5 0 0 1 4.9 6.1l-2-2A3 3 0 0 0 10.9 8l-2-2A8.6 8.6 0 0 1 12 7zm-7.5 5A11 11 0 0 0 12 17a10 10 0 0 0 3.2-.5l-1.7-1.7A5 5 0 0 1 8 9.9L6.4 8.3A11.5 11.5 0 0 0 4.5 12z"/>' +
    '<path fill="currentColor" d="M12 5c5 0 9.3 3.1 11 7a13.7 13.7 0 0 1-2.6 3.6l-1.5-1.5A11 11 0 0 0 21 12c-1.8-2.9-5.1-5-9-5-.5 0-1 0-1.5.1L8.9 5.5C10 5.2 11 5 12 5z" opacity=".55"/>' +
    '</svg>';

  function decorate(input) {
    if (!input || input.dataset.eyeReady === "1") return;
    if (input.type !== "password") return;
    // فیلدهای مخفی سیستمی (توکن/ضدجعل) نباید دکمه بگیرند.
    if (input.name && /token|antiforgery|__RequestVerification/i.test(input.name)) return;

    input.dataset.eyeReady = "1";

    var wrap = document.createElement("span");
    wrap.className = "pw-wrap";
    input.parentNode.insertBefore(wrap, input);
    wrap.appendChild(input);

    var btn = document.createElement("button");
    btn.type = "button";
    btn.className = "pw-eye";
    btn.innerHTML = EYE;
    btn.title = "نمایش رمز";
    btn.setAttribute("aria-label", "نمایش رمز");
    btn.setAttribute("aria-pressed", "false");
    wrap.appendChild(btn);

    btn.addEventListener("click", function () {
      var show = input.type === "password";
      input.type = show ? "text" : "password";
      btn.innerHTML = show ? EYE_OFF : EYE;
      btn.title = show ? "پنهان کردن رمز" : "نمایش رمز";
      btn.setAttribute("aria-label", btn.title);
      btn.setAttribute("aria-pressed", show ? "true" : "false");
      btn.classList.toggle("is-on", show);
      // نگه‌داشتن فوکوس روی همان فیلد تا تایپ ادامه پیدا کند.
      try { input.focus({ preventScroll: true }); } catch (e) { input.focus(); }
      var end = input.value.length;
      try { input.setSelectionRange(end, end); } catch (e) { /* نوع غیرمتنی */ }
    });

    // Esc رمز را دوباره پنهان می‌کند.
    input.addEventListener("keydown", function (event) {
      if (event.key === "Escape" && input.type === "text") {
        btn.click();
        event.stopPropagation();
      }
    });
  }

  function scan(root) {
    var scope = root && root.querySelectorAll ? root : document;
    var nodes = scope.querySelectorAll('input[type="password"]');
    for (var i = 0; i < nodes.length; i++) decorate(nodes[i]);
    if (scope !== document && scope.matches && scope.matches('input[type="password"]')) decorate(scope);
  }

  function boot() {
    scan(document);
    // فیلدهایی که بعداً (مثلاً با پویانما/فیلتر) اضافه می‌شوند.
    if (window.MutationObserver) {
      new MutationObserver(function (records) {
        for (var i = 0; i < records.length; i++) {
          var added = records[i].addedNodes;
          for (var j = 0; j < added.length; j++) {
            if (added[j].nodeType === 1) scan(added[j]);
          }
        }
      }).observe(document.body, { childList: true, subtree: true });
    }
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
  else boot();
})();
