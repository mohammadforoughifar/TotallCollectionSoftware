#!/usr/bin/env bash
# Totall STT Service — اجرا روی لینوکس/مک
# استفاده:  ./run.sh            (پیش‌فرض: مدل small روی پورت 9000)
#           STT_MODEL=medium STT_PORT=9000 ./run.sh
set -euo pipefail
cd "$(dirname "$0")"

STT_MODEL="${STT_MODEL:-small}"
STT_HOST="${STT_HOST:-127.0.0.1}"
STT_PORT="${STT_PORT:-9000}"
STT_LANGUAGE="${STT_LANGUAGE:-fa}"

if [ ! -x ".venv/bin/python" ]; then
  echo "[1/3] Creating Python virtual environment..."
  python3 -m venv .venv
fi
# shellcheck disable=SC1091
source .venv/bin/activate

echo "[2/3] Installing requirements (first run: a few minutes)..."
python -m pip install -q --upgrade pip
python -m pip install -q -r requirements.txt

echo "[3/3] Starting STT service at http://${STT_HOST}:${STT_PORT}  -  Ctrl+C to stop"
echo "      Model: ${STT_MODEL}   (first run downloads the model once)"
echo "      In the app:  Ai:TranscriptionModel=${STT_MODEL}   Ai:TranscriptionBaseUrl=http://127.0.0.1:${STT_PORT}/v1"
echo

EXTRA=()
if [ -n "${STT_API_KEY:-}" ]; then EXTRA=(--api-key "$STT_API_KEY"); fi
exec python stt_server.py --model "$STT_MODEL" --host "$STT_HOST" --port "$STT_PORT" \
  --language "$STT_LANGUAGE" --preload "${EXTRA[@]}"
