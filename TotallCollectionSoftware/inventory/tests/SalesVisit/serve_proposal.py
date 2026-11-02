"""سرویسگر سبک «پیشنمایش طرح/فرمهای ویزیتوری» (بدون .NET / بدون وابستگی خارجی).

    cd TotallCollectionSoftware
    python3 inventory/tests/SalesVisit/serve_proposal.py

مسیرها:
    /            → صفحهٔ طرح پیشنهادی (visit-forms-proposal.html)
    /current     → پیشنمایش نسخهٔ فعلی کامپوننتها (visit-forms-preview.html)
    /src/.../wwwroot/... → داراییهای CSS/فونت/آیکن، دقیقاً همان فایلهای پروژه

نکتهها:
  * GET و HEAD یکسان مسیریابی میشوند تا چکهای سلامت پراکسی هم صفحهٔ درست را ببینند
    (پیادهسازی قبلی فقط GET را هندل میکرد و HEAD پوشهٔ inventory را لیست میکرد).
  * لیست کردن پوشهها مسدود است و فقط زیرشاخهٔ wwwroot سرو میشود.
  * allow_reuse_address روشن است تا راهاندازی مجدد بلافاصله بعد از توقف گیر نکند.
"""
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

INVENTORY = Path(__file__).resolve().parents[2]          # .../inventory
PROPOSAL = Path(__file__).with_name("visit-forms-proposal.html")
CURRENT = Path(__file__).with_name("visit-forms-preview.html")
ASSET_PREFIX = "/src/Inventory.Client/wwwroot/"
PORT = 8000


class ProposalHandler(SimpleHTTPRequestHandler):
    allow_reuse_address = True

    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(INVENTORY), **kwargs)

    def _path(self):
        return self.path.split("?", 1)[0]

    def _route(self):
        path = self._path()
        if path in ("/", "/index.html"):
            return PROPOSAL
        if path in ("/current", "/current.html"):
            return CURRENT
        return None

    def _html(self, target, head=False):
        body = target.read_bytes()
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        if not head:
            self.wfile.write(body)

    def _asset_allowed(self):
        if self._path().startswith(ASSET_PREFIX):
            return True
        self.send_error(404, "Not found")
        return False

    def do_GET(self):
        target = self._route()
        if target is not None:
            self._html(target)
            return
        if self._asset_allowed():
            super().do_GET()

    def do_HEAD(self):
        target = self._route()
        if target is not None:
            self._html(target, head=True)
            return
        if self._asset_allowed():
            super().do_HEAD()

    def list_directory(self, path):
        self.send_error(404, "Directory listing disabled")
        return None

    def log_message(self, fmt, *args):
        pass


if __name__ == "__main__":
    ThreadingHTTPServer.allow_reuse_address = True
    ThreadingHTTPServer.daemon_threads = True
    server = ThreadingHTTPServer(("0.0.0.0", PORT), ProposalHandler)
    print(f"Preview: http://0.0.0.0:{PORT}/  (current: /current)", flush=True)
    server.serve_forever()
