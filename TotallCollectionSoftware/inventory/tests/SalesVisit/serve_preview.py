"""سرویسگر سبک پیشنمایش ایستای فرمهای ویزیتوری (بدون .NET / بدون وابستگی خارجی).

    cd TotallCollectionSoftware
    python3 inventory/tests/SalesVisit/serve_preview.py

سپس ریشهٔ سرور، فایل visit-forms-preview.html را نشان میدهد؛ فایلهای CSS/فونت
هم از همان wwwroot پروژه سرو میشوند تا پیشنمایش دقیقاً همان استایلها را بگیرد.
"""
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

INVENTORY = Path(__file__).resolve().parents[2]          # .../inventory
PREVIEW = Path(__file__).with_name("visit-forms-preview.html")
PORT = 8000


class PreviewHandler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(INVENTORY), **kwargs)

    def do_GET(self):
        if self.path.split("?", 1)[0] in ("/", "/index.html"):
            body = PREVIEW.read_bytes()
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(body)
            return
        super().do_GET()

    def log_message(self, fmt, *args):
        pass


if __name__ == "__main__":
    print(f"Preview: http://0.0.0.0:{PORT}/  (root={INVENTORY})", flush=True)
    ThreadingHTTPServer(("0.0.0.0", PORT), PreviewHandler).serve_forever()
