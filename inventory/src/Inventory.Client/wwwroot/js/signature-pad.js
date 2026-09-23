// ============================================================
//  پنل امضای الکترونیکی (canvas) — ماژول صورتجلسه
//  پشتیبانی موس و لمس (pointer events)؛ خروجی PNG شفاف base64
// ============================================================
window.sigPadInit = function (canvas) {
    if (!canvas) return false;
    var ctx = canvas.getContext('2d');
    var drawing = false;
    var color = '#1e3a8a';
    var lineWidth = 2.5;

    function pos(e) {
        var r = canvas.getBoundingClientRect();
        return {
            x: (e.clientX - r.left) * (canvas.width / r.width),
            y: (e.clientY - r.top) * (canvas.height / r.height)
        };
    }

    canvas.style.touchAction = 'none';
    canvas.style.cursor = 'crosshair';

    canvas.addEventListener('pointerdown', function (e) {
        e.preventDefault();
        drawing = true;
        try { canvas.setPointerCapture(e.pointerId); } catch (err) { }
        var p = pos(e);
        ctx.beginPath();
        ctx.moveTo(p.x, p.y);
        ctx.lineTo(p.x + 0.1, p.y + 0.1);
        ctx.strokeStyle = color;
        ctx.lineWidth = lineWidth;
        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';
        ctx.stroke();
    });

    canvas.addEventListener('pointermove', function (e) {
        if (!drawing) return;
        e.preventDefault();
        var p = pos(e);
        ctx.lineTo(p.x, p.y);
        ctx.stroke();
    });

    function end() { drawing = false; }
    canvas.addEventListener('pointerup', end);
    canvas.addEventListener('pointercancel', end);
    canvas.addEventListener('pointerleave', end);

    return true;
};

window.sigPadClear = function (canvas) {
    if (!canvas) return;
    var ctx = canvas.getContext('2d');
    ctx.clearRect(0, 0, canvas.width, canvas.height);
};

window.sigPadEmpty = function (canvas) {
    if (!canvas) return true;
    var d = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data;
    for (var i = 3; i < d.length; i += 4) if (d[i] !== 0) return false;
    return true;
};

window.sigPadToBase64 = function (canvas) {
    if (!canvas) return '';
    return canvas.toDataURL('image/png');
};

// دانلود فایل از base64 (برای PDF چاپی و...)
window.downloadBase64 = function (fileName, dataBase64, mime) {
    try {
        var bytes = atob(dataBase64);
        var arr = new Uint8Array(bytes.length);
        for (var i = 0; i < bytes.length; i++) arr[i] = bytes.charCodeAt(i);
        var blob = new Blob([arr], { type: mime || 'application/octet-stream' });
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = fileName || 'download';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        setTimeout(function () { URL.revokeObjectURL(url); }, 4000);
        return true;
    } catch (e) {
        return false;
    }
};
