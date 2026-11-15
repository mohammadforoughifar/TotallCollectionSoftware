/* ─────────────────────────────────────────────────────────────────────────────
 * تقویم شمسی (جلالی) سبک و بدون وابستگی — برای پنل پشتیبانی لایسنس
 * استفاده: به هر <input> کلاس «jdate» بدهید. مقدار با ارقام فارسی و به شکل
 * «۱۴۰۵/۰۸/۲۴» نوشته می‌شود (همان قالبی که سرور با JalaliDate.TryParse می‌خواند).
 * هیچ درخواست شبکه‌ای ندارد؛ در محیط بدون اینترنت هم کار می‌کند.
 * ───────────────────────────────────────────────────────────────────────────── */
(function () {
  "use strict";

  var MONTHS = ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
                "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];
  var WEEKDAYS = ["ش", "ی", "د", "س", "چ", "پ", "ج"];
  var FA_DIGITS = "۰۱۲۳۴۵۶۷۸۹";

  function toFa(value) {
    return String(value).replace(/[0-9]/g, function (d) { return FA_DIGITS[+d]; });
  }
  function toEn(value) {
    return String(value).replace(/[۰-۹]/g, function (d) { return String(FA_DIGITS.indexOf(d)); })
                        .replace(/[٠-٩]/g, function (d) { return String("٠١٢٣٤٥٦٧٨٩".indexOf(d)); });
  }

  /* تبدیل میلادی ↔ جلالی (الگوریتم استاندارد، بدون کتابخانه) */
  function div(a, b) { return Math.floor(a / b); }
  function g2j(gy, gm, gd) {
    var g_d_m = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334];
    var jy = (gy <= 1600) ? 0 : 979;
    gy -= (gy <= 1600) ? 621 : 1600;
    var gy2 = (gm > 2) ? (gy + 1) : gy;
    var days = (365 * gy) + div(gy2 + 3, 4) - div(gy2 + 99, 100) + div(gy2 + 399, 400)
             - 80 + gd + g_d_m[gm - 1];
    jy += 33 * div(days, 12053);
    days %= 12053;
    jy += 4 * div(days, 1461);
    days %= 1461;
    if (days > 365) { jy += div(days - 1, 365); days = (days - 1) % 365; }
    var jm = (days < 186) ? 1 + div(days, 31) : 7 + div(days - 186, 30);
    var jd = 1 + ((days < 186) ? (days % 31) : ((days - 186) % 30));
    return [jy, jm, jd];
  }
  function j2g(jy, jm, jd) {
    var gy = (jy <= 979) ? 621 : 1600;
    jy -= (jy <= 979) ? 0 : 979;
    var days = (365 * jy) + (div(jy, 33) * 8) + div((jy % 33) + 3, 4) + 78 + jd
             + ((jm < 7) ? ((jm - 1) * 31) : (((jm - 7) * 30) + 186));
    gy += 400 * div(days, 146097);
    days %= 146097;
    if (days > 36524) {
      gy += 100 * div(--days, 36524);
      days %= 36524;
      if (days >= 365) days++;
    }
    gy += 4 * div(days, 1461);
    days %= 1461;
    if (days > 365) { gy += div(days - 1, 365); days = (days - 1) % 365; }
    var gd = days + 1;
    var sal_a = [0, 31, (gy % 4 === 0 && (gy % 100 !== 0 || gy % 400 === 0)) ? 29 : 28,
                 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    var gm = 0;
    for (gm = 1; gm <= 12 && gd > sal_a[gm]; gm++) gd -= sal_a[gm];
    return [gy, gm, gd];
  }
  function monthLength(jy, jm) {
    if (jm <= 6) return 31;
    if (jm <= 11) return 30;
    /* اسفند: کبیسه اگر باقی‌ماندهٔ سال (به روش ۳۳ساله) یکی از این‌ها باشد */
    var r = jy % 33;
    return ([1, 5, 9, 13, 17, 22, 26, 30].indexOf(r) >= 0) ? 30 : 29;
  }
  function todayJalali() { var d = new Date(); return g2j(d.getFullYear(), d.getMonth() + 1, d.getDate()); }
  function weekdayIndex(jy, jm, jd) { /* 0 = شنبه */
    var g = j2g(jy, jm, jd);
    var wd = new Date(g[0], g[1] - 1, g[2]).getDay(); /* 0=یکشنبه */
    return (wd + 1) % 7;
  }
  function fa2lat(str) {
    return toEn(str).replace(/[^\d\/]/g, "");
  }
  function parseJalali(text) {
    var parts = fa2lat(text).split("/");
    if (parts.length !== 3) return null;
    var jy = +parts[0], jm = +parts[1], jd = +parts[2];
    if (!jy || jy < 1300 || jy > 1500 || jm < 1 || jm > 12 || jd < 1 || jd > 31) return null;
    return [jy, jm, jd];
  }

  var popup = null, target = null, view = null;

  function close() {
    if (popup) { popup.remove(); popup = null; }
    target = null;
  }

  function build() {
    var el = document.createElement("div");
    el.className = "jdp";
    el.setAttribute("role", "dialog");
    el.setAttribute("aria-label", "انتخاب تاریخ شمسی");
    el.innerHTML =
      '<div class="jdp-head">' +
        '<button type="button" class="jdp-nav" data-step="-12" title="سال قبل">‹‹</button>' +
        '<button type="button" class="jdp-nav" data-step="-1" title="ماه قبل">‹</button>' +
        '<span class="jdp-title"></span>' +
        '<button type="button" class="jdp-nav" data-step="1" title="ماه بعد">›</button>' +
        '<button type="button" class="jdp-nav" data-step="12" title="سال بعد">››</button>' +
      "</div>" +
      '<div class="jdp-week">' + WEEKDAYS.map(function (w) { return "<span>" + w + "</span>"; }).join("") + "</div>" +
      '<div class="jdp-grid"></div>' +
      '<div class="jdp-foot">' +
        '<button type="button" class="jdp-btn" data-act="today">امروز</button>' +
        '<button type="button" class="jdp-btn ghost" data-act="clear">پاک‌کردن</button>' +
        '<button type="button" class="jdp-btn ghost" data-act="close">بستن</button>' +
      "</div>";
    return el;
  }

  function paint() {
    if (!popup || !view) return;
    popup.querySelector(".jdp-title").textContent = MONTHS[view[1] - 1] + " " + toFa(view[0]);
    var grid = popup.querySelector(".jdp-grid");
    grid.innerHTML = "";
    var len = monthLength(view[0], view[1]);
    var first = weekdayIndex(view[0], view[1], 1);
    var selected = target ? parseJalali(target.value) : null;
    var today = todayJalali();
    for (var i = 0; i < first; i++) {
      var pad = document.createElement("span");
      pad.className = "jdp-pad";
      grid.appendChild(pad);
    }
    for (var d = 1; d <= len; d++) {
      var cell = document.createElement("button");
      cell.type = "button";
      cell.className = "jdp-day";
      cell.textContent = toFa(d);
      cell.dataset.day = d;
      if (selected && selected[0] === view[0] && selected[1] === view[1] && selected[2] === d) cell.classList.add("sel");
      if (today[0] === view[0] && today[1] === view[1] && today[2] === d) cell.classList.add("today");
      grid.appendChild(cell);
    }
  }

  function openFor(input) {
    close();
    target = input;
    popup = build();
    document.body.appendChild(popup);
    var current = parseJalali(input.value) || todayJalali();
    view = [current[0], current[1]];
    paint();
    position();
    popup.addEventListener("click", onClick);
    document.addEventListener("mousedown", onOutside, true);
    document.addEventListener("keydown", onKey, true);
  }

  function position() {
    if (!popup || !target) return;
    var box = target.getBoundingClientRect();
    var width = popup.offsetWidth, height = popup.offsetHeight;
    var left = Math.min(window.scrollX + box.left, window.scrollX + document.documentElement.clientWidth - width - 8);
    var top = window.scrollY + box.bottom + 6;
    if (box.bottom + height + 12 > document.documentElement.clientHeight) {
      top = window.scrollY + box.top - height - 6;
    }
    popup.style.left = Math.max(8, left) + "px";
    popup.style.top = Math.max(8, top) + "px";
  }

  function onClick(event) {
    var navBtn = event.target.closest("[data-step]");
    if (navBtn) {
      var step = +navBtn.dataset.step;
      var total = (view[0] * 12 + (view[1] - 1)) + step;
      view = [Math.floor(total / 12), (total % 12) + 1];
      paint();
      return;
    }
    var day = event.target.closest(".jdp-day");
    if (day) {
      var value = toFa(view[0]) + "/" + toFa(("0" + view[1]).slice(-2)) + "/" + toFa(("0" + day.dataset.day).slice(-2));
      target.value = value;
      target.dispatchEvent(new Event("input", { bubbles: true }));
      target.dispatchEvent(new Event("change", { bubbles: true }));
      close();
      return;
    }
    var act = event.target.closest("[data-act]");
    if (act) {
      var which = act.dataset.act;
      if (which === "today") {
        var t = todayJalali();
        target.value = toFa(t[0]) + "/" + toFa(("0" + t[1]).slice(-2)) + "/" + toFa(("0" + t[2]).slice(-2));
        view = [t[0], t[1]];
        target.dispatchEvent(new Event("change", { bubbles: true }));
        paint();
      } else if (which === "clear") {
        target.value = "";
        target.dispatchEvent(new Event("change", { bubbles: true }));
        close();
      } else {
        close();
      }
    }
  }

  function onOutside(event) {
    if (popup && !popup.contains(event.target) && event.target !== target) cleanup();
  }
  function onKey(event) {
    if (event.key === "Escape") cleanup();
  }
  function cleanup() {
    document.removeEventListener("mousedown", onOutside, true);
    document.removeEventListener("keydown", onKey, true);
    close();
  }

  function enhance(input) {
    if (input.dataset.jdpReady === "1") return;
    input.dataset.jdpReady = "1";
    input.setAttribute("autocomplete", "off");
    input.setAttribute("placeholder", input.getAttribute("placeholder") || "۱۴۰۵/۰۸/۲۴");
    input.addEventListener("focus", function () { openFor(input); });
    input.addEventListener("click", function () { openFor(input); });
    input.addEventListener("keydown", function (e) {
      if (e.key === "Escape") cleanup();
      /* فقط ارقام و اسلش پذیرفته شود */
      if (e.key.length === 1 && !/[0-9\/۰-۹]/.test(e.key) && !e.ctrlKey && !e.metaKey) e.preventDefault();
    });
  }

  function init() {
    document.querySelectorAll("input.jdate").forEach(enhance);
  }

  document.addEventListener("DOMContentLoaded", init);
  window.jdatepicker = { init: init, toFa: toFa, today: function () {
    var t = todayJalali();
    return toFa(t[0]) + "/" + toFa(("0" + t[1]).slice(-2)) + "/" + toFa(("0" + t[2]).slice(-2));
  } };
})();
