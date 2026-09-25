// =====================================================================
// دستیار فروغ آریا — ورودی صوتی فارسی (Web Speech API مرورگر) + اسکرول چت
// بدون نیاز به سرور؛ در کروم/اج دسکتاپ و اندروید کار می‌کند.
// =====================================================================
window.aiVoice = (function () {
    var rec = null;
    var listening = false;

    function ctor() {
        var SR = window.SpeechRecognition || window.webkitSpeechRecognition;
        return SR ? new SR() : null;
    }

    return {
        isSupported: function () {
            return !!(window.SpeechRecognition || window.webkitSpeechRecognition);
        },
        isListening: function () { return listening; },
        start: function (dotNetRef) {
            if (listening) return true;
            rec = ctor();
            if (!rec) return false;
            rec.lang = 'fa-IR';
            rec.interimResults = false;
            rec.maxAlternatives = 1;
            rec.onresult = function (e) {
                try {
                    var text = e.results && e.results[0] && e.results[0][0] ? e.results[0][0].transcript : '';
                    if (text) dotNetRef.invokeMethodAsync('OnVoiceResult', text);
                } catch (err) { /* نادیده */ }
            };
            rec.onerror = function (e) {
                try { dotNetRef.invokeMethodAsync('OnVoiceEnd', (e && e.error) || 'error'); } catch (err) { }
            };
            rec.onend = function () {
                listening = false;
                try { dotNetRef.invokeMethodAsync('OnVoiceEnd', ''); } catch (err) { }
            };
            try {
                rec.start();
                listening = true;
                return true;
            } catch (err) {
                listening = false;
                return false;
            }
        },
        stop: function () {
            try { if (rec && listening) rec.stop(); } catch (err) { }
            listening = false;
        }
    };
})();

window.aiChat = {
    scrollBottom: function (id) {
        try {
            var el = document.getElementById(id);
            if (el) el.scrollTop = el.scrollHeight;
        } catch (err) { }
    }
};
