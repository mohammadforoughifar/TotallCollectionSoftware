#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Totall STT Service — سرویس محلی تبدیل گفتار به متن (Speech To Text)
===================================================================
این سرویس پیام‌های صوتی پیام‌رسان (چت وب) را به متن فارسی تبدیل می‌کند و
دقیقاً با «قرارداد OpenAI» کار می‌کند، یعنی همان چیزی که سامانه انتظار دارد:

    POST /v1/audio/transcriptions     (multipart/form-data)
        file            = فایل صوتی (webm / m4a / ogg / mp3 / wav / ...)
        model           = نام مدل (اطلاعیه؛ مدل واقعی با --model تعیین می‌شود)
        language        = fa
        response_format = json | text | srt | vtt | verbose_json

    پاسخ: {"text": "متن پیام"}

سایر آدرس‌ها:
    GET /health      → وضعیت سرویس و مدل
    GET /v1/models   → فهرست مدل‌ها (سازگار با کلاینت‌های OpenAI)

اجرا:  python stt_server.py --model small --port 9000
وابستگی‌ها: فقط faster-whisper  (pip install -r requirements.txt)

نکته: مدل‌ها بار اول از اینترنت دانلود و در پوشهٔ ./models ذخیره می‌شوند؛
بارهای بعدی حتی بدون اینترنت کار می‌کند.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import secrets
import sys
import tempfile
import threading
import time
import traceback
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any, Dict, List, Optional, Tuple
from urllib.parse import unquote

VERSION = "1.0.0"
DEFAULT_PORT = 9000

# ---------------------------------------------------------------------------
# پارس کردن multipart/form-data با کتابخانهٔ استاندارد پایتون (بدون وابستگی اضافه)
# ---------------------------------------------------------------------------


def _disposition_params(header_value: str) -> Dict[str, str]:
    """پارامترهاییکه به ``;`` از هم جدا شدهاند را میخواند.

    نکته: .NET نام و فایل را بدون کوتیشن میفرستد (``name=file; filename=a.webm``)
    ولی مرورگرها/کلاینتهای دیگر با کوتیشن (``name="file"``) میفرستند؛ هر دو پشتیبانی میشود.
    """
    params: Dict[str, str] = {}
    pattern = re.compile(r';\s*([A-Za-z0-9*_.-]+)\s*=\s*(?:"([^"]*)"|([^;]*))')
    for match in pattern.finditer(header_value):
        key = match.group(1).lower()
        value = match.group(2) if match.group(2) is not None else (match.group(3) or "")
        params[key] = value.strip()
    return params


def parse_multipart(content_type: str, body: bytes) -> Dict[str, Dict[str, Any]]:
    match = re.search(r'boundary="?([^";,]+)"?', content_type or "")
    if not match:
        raise ValueError("Content-Type must be multipart/form-data with a boundary")

    boundary = match.group(1).strip().encode("utf-8")
    delimiter = b"--" + boundary
    fields: Dict[str, Dict[str, Any]] = {}

    for chunk in body.split(delimiter)[1:]:
        if chunk[:2] == b"--":  # پایان بدنه
            break
        if chunk.startswith(b"\r\n"):
            chunk = chunk[2:]
        sep = chunk.find(b"\r\n\r\n")
        if sep < 0:
            continue
        raw_headers = chunk[:sep].decode("utf-8", "replace")
        data = chunk[sep + 4 :]
        if data.endswith(b"\r\n"):
            data = data[:-2]

        name: Optional[str] = None
        filename: Optional[str] = None
        part_type: Optional[str] = None
        for line in raw_headers.split("\r\n"):
            low = line.lower()
            if low.startswith("content-disposition:"):
                params = _disposition_params(line)
                name = params.get("name")
                filename = params.get("filename")
                extended = params.get("filename*")
                if extended:  # filename*=UTF-8''%D9%81%D8%A7%DB%8C%D9%84 → ترجیح داده می‌شود
                    if "''" in extended:
                        extended = extended.split("''", 1)[1]
                    filename = unquote(extended)
            elif low.startswith("content-type:"):
                part_type = line.split(":", 1)[1].strip()

        if name:
            fields[name] = {"filename": filename, "content_type": part_type, "data": data}
    return fields


# ---------------------------------------------------------------------------
# موتور تبدیل گفتار به متن (faster-whisper)
# ---------------------------------------------------------------------------


class Transcriber:
    """مدل را یک‌بار (تنبل/با --preload) بارگذاری و درخواست‌ها را سریالی می‌کند."""

    def __init__(self, args: argparse.Namespace) -> None:
        self.model_name = args.model
        self.model_dir = os.path.abspath(args.model_dir)
        self.device = args.device
        self.compute_type = args.compute_type
        self.cpu_threads = args.cpu_threads
        self.language = args.language
        self.beam_size = args.beam_size
        self.vad = args.vad
        self.initial_prompt = args.initial_prompt
        self._model = None
        self._lock = threading.Lock()
        self._load_lock = threading.Lock()

    # -- بارگذاری ---------------------------------------------------------
    @property
    def loaded(self) -> bool:
        return self._model is not None

    def load(self) -> None:
        if self._model is not None:
            return
        with self._load_lock:
            if self._model is not None:
                return
            started = time.time()
            log(f"loading model '{self.model_name}' (device={self.device}, compute_type={self.compute_type}) ...")
            log(f"model cache directory: {self.model_dir}")
            from faster_whisper import WhisperModel  # import دیرهنگام: /health سریع جواب می‌دهد

            os.makedirs(self.model_dir, exist_ok=True)
            kwargs: Dict[str, Any] = {
                "device": self.device,
                "compute_type": self.compute_type,
                "download_root": self.model_dir,
            }
            if self.cpu_threads > 0:
                kwargs["cpu_threads"] = self.cpu_threads

            self._model = WhisperModel(self.model_name, **kwargs)
            log(f"model ready in {time.time() - started:.1f}s")

    # -- تبدیل ------------------------------------------------------------
    def transcribe(
        self,
        audio_path: str,
        language: Optional[str],
        prompt: Optional[str],
    ) -> Dict[str, Any]:
        self.load()
        lang = (language or self.language or "").strip() or None
        started = time.time()
        with self._lock:  # یک تبدیل در هر لحظه (مدل thread-safe نیست)
            segments, info = self._model.transcribe(  # type: ignore[union-attr]
                audio_path,
                language=lang,
                beam_size=self.beam_size,
                vad_filter=self.vad,
                initial_prompt=prompt or self.initial_prompt,
                condition_on_previous_text=False,
            )
            rows: List[Dict[str, Any]] = []
            pieces: List[str] = []
            for segment in segments:
                text = (segment.text or "").strip()
                if not text:
                    continue
                pieces.append(text)
                rows.append(
                    {
                        "id": len(rows),
                        "start": round(float(segment.start), 2),
                        "end": round(float(segment.end), 2),
                        "text": text,
                    }
                )
        return {
            "text": " ".join(pieces).strip(),
            "language": getattr(info, "language", lang) or lang or "",
            "duration": round(float(getattr(info, "duration", 0.0) or 0.0), 2),
            "elapsed": round(time.time() - started, 2),
            "segments": rows,
        }


# ---------------------------------------------------------------------------
# قالب‌های خروجی
# ---------------------------------------------------------------------------


def _stamp(seconds: float, comma: bool) -> str:
    ms = int(round((seconds - int(seconds)) * 1000))
    total = int(seconds)
    h, rem = divmod(total, 3600)
    m, s = divmod(rem, 60)
    sep = "," if comma else "."
    return f"{h:02d}:{m:02d}:{s:02d}{sep}{ms:03d}"


def to_srt(segments: List[Dict[str, Any]]) -> str:
    lines: List[str] = []
    for index, seg in enumerate(segments, start=1):
        lines.append(str(index))
        lines.append(f"{_stamp(seg['start'], True)} --> {_stamp(seg['end'], True)}")
        lines.append(seg["text"])
        lines.append("")
    return "\n".join(lines)


def to_vtt(segments: List[Dict[str, Any]]) -> str:
    lines = ["WEBVTT", ""]
    for seg in segments:
        lines.append(f"{_stamp(seg['start'], False)} --> {_stamp(seg['end'], False)}")
        lines.append(seg["text"])
        lines.append("")
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# سرور HTTP
# ---------------------------------------------------------------------------

ARGS: argparse.Namespace
TRANSCRIBER: Transcriber


def secrets_token(nbytes: int = 4) -> str:
    return secrets.token_hex(nbytes)


def log(message: str) -> None:
    print(time.strftime("[%Y-%m-%d %H:%M:%S] ") + message, flush=True)


def json_error(message: str, status: int) -> Tuple[int, bytes, str]:
    payload = {"error": {"message": message, "type": "invalid_request_error", "param": None, "code": None}}
    return status, json.dumps(payload, ensure_ascii=False).encode("utf-8"), "application/json; charset=utf-8"


class Handler(BaseHTTPRequestHandler):
    server_version = f"TotallSTT/{VERSION}"
    protocol_version = "HTTP/1.1"

    # -- ابزارها ----------------------------------------------------------
    def _send(self, status: int, body: bytes, content_type: str) -> None:
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Headers", "Authorization, Content-Type")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def _send_json(self, status: int, payload: Dict[str, Any]) -> None:
        self._send(status, json.dumps(payload, ensure_ascii=False).encode("utf-8"), "application/json; charset=utf-8")

    def _authorized(self) -> bool:
        if not ARGS.api_key:
            return True
        header = (self.headers.get("Authorization") or "").strip()
        token = header[7:].strip() if header.lower().startswith("bearer ") else ""
        return token == ARGS.api_key

    def log_message(self, fmt: str, *args: Any) -> None:  # noqa: A003 - صدای پیش‌فرض را خاموش می‌کنیم
        return

    # -- GET --------------------------------------------------------------
    def do_GET(self) -> None:  # noqa: N802
        path = self.path.split("?", 1)[0].rstrip("/") or "/"
        if path in ("/", "/health", "/healthz", "/v1/health"):
            self._send_json(
                200,
                {
                    "status": "ok",
                    "service": "totall-stt",
                    "version": VERSION,
                    "backend": "faster-whisper",
                    "model": TRANSCRIBER.model_name,
                    "device": TRANSCRIBER.device,
                    "compute_type": TRANSCRIBER.compute_type,
                    "model_loaded": TRANSCRIBER.loaded,
                    "language_default": TRANSCRIBER.language or "auto",
                    "vad": TRANSCRIBER.vad,
                    "endpoint": "/v1/audio/transcriptions",
                },
            )
            return
        if path in ("/v1/models", "/models"):
            self._send_json(
                200,
                {
                    "object": "list",
                    "data": [
                        {
                            "id": TRANSCRIBER.model_name,
                            "object": "model",
                            "created": int(time.time()),
                            "owned_by": "local",
                        }
                    ],
                },
            )
            return
        status, body, ctype = json_error("Unknown path: " + path, 404)
        self._send(status, body, ctype)

    def do_OPTIONS(self) -> None:  # noqa: N802
        self._send(204, b"", "text/plain; charset=utf-8")

    # -- POST -------------------------------------------------------------
    def do_POST(self) -> None:  # noqa: N802
        path = self.path.split("?", 1)[0].rstrip("/")
        if path not in ("/v1/audio/transcriptions", "/audio/transcriptions"):
            status, body, ctype = json_error("Unknown path: " + path, 404)
            self._send(status, body, ctype)
            return

        if not self._authorized():
            status, body, ctype = json_error("Invalid API key", 401)
            self._send(status, body, ctype)
            return

        try:
            length = int(self.headers.get("Content-Length") or 0)
        except ValueError:
            length = 0
        if length <= 0:
            status, body, ctype = json_error("Empty request body", 400)
            self._send(status, body, ctype)
            return
        if length > ARGS.max_upload_mb * 1024 * 1024:
            status, body, ctype = json_error(f"Payload too large (limit {ARGS.max_upload_mb} MB)", 413)
            self._send(status, body, ctype)
            return

        raw = self._read_body(length)
        if raw is None:
            return  # پاسخ خطا قبلاً ارسال شده

        log(f"request received: {length} bytes, content_type={self.headers.get('Content-Type', '-')}")
        if ARGS.dump_dir:
            self._dump_request(raw)

        try:
            fields = parse_multipart(self.headers.get("Content-Type", ""), raw)
        except Exception as ex:  # noqa: BLE001
            status, body, ctype = json_error("Invalid multipart body: " + str(ex), 400)
            self._send(status, body, ctype)
            return

        part = fields.get("file")
        if not part or not part.get("data"):
            status, body, ctype = json_error("Missing 'file' field (multipart/form-data)", 400)
            self._send(status, body, ctype)
            return

        language = _field_text(fields.get("language"))
        prompt = _field_text(fields.get("prompt"))
        fmt = (_field_text(fields.get("response_format")) or "json").lower()
        client_model = _field_text(fields.get("model"))

        suffix = os.path.splitext(part.get("filename") or "audio")[1] or ".bin"
        temp_path = None
        try:
            with tempfile.NamedTemporaryFile(delete=False, suffix=suffix) as handle:
                handle.write(part["data"])
                temp_path = handle.name

            log(
                f"transcribe request: {len(part['data']) / 1024:.0f} KB, filename={part.get('filename') or '-'}, "
                f"content_type={part.get('content_type') or '-'}, language={language or ARGS.language or 'auto'}, "
                f"response_format={fmt}, client_model={client_model or '-'}"
            )
            result = TRANSCRIBER.transcribe(temp_path, language, prompt)
            text = result["text"]
            log(f"transcribe done in {result['elapsed']}s: {len(text)} chars, detected={result['language']}")

            if fmt == "text":
                self._send(200, text.encode("utf-8"), "text/plain; charset=utf-8")
            elif fmt == "srt":
                self._send(200, to_srt(result["segments"]).encode("utf-8"), "text/plain; charset=utf-8")
            elif fmt == "vtt":
                self._send(200, to_vtt(result["segments"]).encode("utf-8"), "text/plain; charset=utf-8")
            elif fmt == "verbose_json":
                self._send_json(
                    200,
                    {
                        "task": "transcribe",
                        "language": result["language"],
                        "duration": result["duration"],
                        "text": text,
                        "segments": result["segments"],
                    },
                )
            else:  # json
                self._send_json(200, {"text": text})
        except Exception as ex:  # noqa: BLE001
            log("transcription failed: " + repr(ex))
            traceback.print_exc()
            status, body, ctype = json_error(f"Transcription failed: {ex}", 500)
            self._send(status, body, ctype)
        finally:
            if temp_path:
                try:
                    os.remove(temp_path)
                except OSError:
                    pass

    def _dump_request(self, raw: bytes) -> None:
        """ذخیرهٔ درخواست خام برای اشکال‌زدایی (--dump-dir)."""
        try:
            os.makedirs(ARGS.dump_dir, exist_ok=True)
            name = time.strftime("%Y%m%d-%H%M%S") + "-" + secrets_token(4)
            with open(os.path.join(ARGS.dump_dir, name + ".headers.txt"), "w", encoding="utf-8") as handle:
                handle.write(self.requestline + "\r\n")
                handle.write(str(self.headers))
            with open(os.path.join(ARGS.dump_dir, name + ".body.bin"), "wb") as handle:
                handle.write(raw)
            log("raw request saved to " + os.path.join(ARGS.dump_dir, name + ".body.bin"))
        except Exception as ex:  # noqa: BLE001
            log("could not dump request: " + repr(ex))

    def _read_body(self, length: int) -> Optional[bytes]:
        try:
            return self.rfile.read(length)
        except Exception as ex:  # noqa: BLE001
            log("failed to read body: " + repr(ex))
            status, body, ctype = json_error("Failed to read request body", 400)
            try:
                self._send(status, body, ctype)
            except OSError:
                pass
            return None


def apply_av_compat_patch() -> None:
    """سازگاری خودکار با نسخه‌های جدید PyAV.

    faster-whisper ۱.۲ آرگومان ``metadata_errors="ignore"`` را به ``av.open`` می‌فرستد،
    ولی PyAV 19 این آرگومان را برداشته است و خطای
    ``open() got an unexpected keyword argument 'metadata_errors'`` می‌دهد.
    این وصله فقط وقتی لازم باشد آن آرگومان را حذف می‌کند (روی PyAV قدیمی دست نمی‌زند).
    """
    try:
        import av
        from faster_whisper import audio as faster_audio
    except Exception:  # noqa: BLE001 - نبودِ هر کدام را در جای دیگر گزارش می‌کنیم
        return

    original = av.open
    if getattr(original, "_totall_av_compat", False):
        return

    def _accepts_kwarg() -> bool:
        try:
            av.open(os.devnull, metadata_errors="ignore")
            return True
        except TypeError:
            return False
        except Exception:  # noqa: BLE001 - خطای فایل/فرمت یعنی آرگومان پذیرفته شده است
            return True

    if _accepts_kwarg():
        return

    def open_compat(*args: Any, **kwargs: Any) -> Any:
        kwargs.pop("metadata_errors", None)
        return original(*args, **kwargs)

    open_compat._totall_av_compat = True  # type: ignore[attr-defined]
    av.open = open_compat  # type: ignore[assignment]
    faster_audio.av = av
    log("applied PyAV compatibility patch (metadata_errors removed)")


def _field_text(part: Optional[Dict[str, Any]]) -> Optional[str]:
    if not part:
        return None
    raw = part.get("data") or b""
    try:
        return raw.decode("utf-8", "replace").strip()
    except Exception:  # noqa: BLE001
        return None


# ---------------------------------------------------------------------------
# راه‌اندازی
# ---------------------------------------------------------------------------


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Totall STT Service — سرویس محلی تبدیل گفتار به متن (سازگار با OpenAI)",
        formatter_class=argparse.ArgumentDefaultsHelpFormatter,
    )
    parser.add_argument("--model", default="small", help="tiny | base | small | medium | large-v3 | large-v3-turbo | مسیر مدل")
    parser.add_argument("--model-dir", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "models"), help="پوشهٔ ذخیرهٔ مدل‌ها")
    parser.add_argument("--host", default="127.0.0.1", help="0.0.0.0 = دسترسی از شبکه")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--device", default="auto", help="auto | cpu | cuda")
    parser.add_argument("--compute-type", default="", help="خالی = int8 روی CPU و float16 روی GPU")
    parser.add_argument("--cpu-threads", type=int, default=0, help="0 = خودکار")
    parser.add_argument("--language", default="", help="خالی = تشخیص خودکار (سامانه fa می‌فرستد)")
    parser.add_argument("--beam-size", type=int, default=5)
    parser.add_argument(
        "--initial-prompt",
        default="این یک پیام صوتی فارسی است.",
        help='متن راهنما برای بهبود دقت و املای فارسی؛ برای خاموش‌کردن: --initial-prompt ""',
    )
    parser.add_argument("--no-vad", dest="vad", action="store_false", help="خاموش کردن حذف سکوت")
    parser.set_defaults(vad=True)
    parser.add_argument("--api-key", default="", help="خالی = بدون احراز هویت (فقط برای شبکهٔ داخلی)")
    parser.add_argument("--preload", action="store_true", help="بارگذاری مدل هنگام شروع (نه در اولین درخواست)")
    parser.add_argument("--max-upload-mb", type=int, default=32)
    parser.add_argument("--dump-dir", default="", help="ذخیرهٔ درخواست‌های خام برای اشکال‌زدایی")
    return parser


def main(argv: Optional[List[str]] = None) -> int:
    global ARGS, TRANSCRIBER
    ARGS = build_parser().parse_args(argv)

    if not ARGS.compute_type:
        ARGS.compute_type = "int8" if ARGS.device in ("cpu", "auto") else "float16"
    if ARGS.device == "auto":
        try:
            import ctranslate2  # noqa: F401

            has_cuda = ctranslate2.get_cuda_device_count() > 0
        except Exception:  # noqa: BLE001
            has_cuda = False
        ARGS.device = "cuda" if has_cuda else "cpu"
        if ARGS.device == "cpu" and ARGS.compute_type == "float16":
            ARGS.compute_type = "int8"

    try:
        import faster_whisper  # noqa: F401
    except ImportError:
        print(
            "ERROR: faster-whisper is not installed.\n"
            "       Run:  pip install -r requirements.txt",
            file=sys.stderr,
        )
        return 2

    apply_av_compat_patch()

    TRANSCRIBER = Transcriber(ARGS)
    if ARGS.preload:
        try:
            TRANSCRIBER.load()
        except Exception as ex:  # noqa: BLE001
            print(f"ERROR: could not load model '{ARGS.model}': {ex}", file=sys.stderr)
            return 3

    server = ThreadingHTTPServer((ARGS.host, ARGS.port), Handler)
    server.daemon_threads = True

    log(f"Totall STT Service v{VERSION} listening on http://{ARGS.host}:{ARGS.port}")
    log(f"endpoint : POST http://{ARGS.host}:{ARGS.port}/v1/audio/transcriptions")
    log(f"health   : GET  http://{ARGS.host}:{ARGS.port}/health")
    log(f"model    : {ARGS.model} ({ARGS.device}/{ARGS.compute_type}, vad={'on' if ARGS.vad else 'off'})")
    log("در سامانه:  Ai:TranscriptionModel = " + ARGS.model + "   و   Ai:TranscriptionBaseUrl = http://127.0.0.1:%d/v1" % ARGS.port)
    log("Ctrl+C to stop.")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        log("stopping ...")
    finally:
        server.server_close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
