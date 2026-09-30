@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
title Totall STT Service - تبدیل گفتار به متن

set "VENVPY=.venv\Scripts\python.exe"

if exist "%VENVPY%" goto venv_ok
echo [1/3] Creating Python virtual environment...
python -m venv .venv
if errorlevel 1 goto err
:venv_ok
call ".venv\Scripts\activate.bat"

echo [2/3] Installing requirements (first run: a few minutes)...
python -m pip install -q --upgrade pip
python -m pip install -q -r requirements.txt
if errorlevel 1 goto err

if not defined STT_MODEL set "STT_MODEL=small"
if not defined STT_HOST  set "STT_HOST=127.0.0.1"
if not defined STT_PORT  set "STT_PORT=9000"
if not defined STT_LANGUAGE set "STT_LANGUAGE=fa"

echo [3/3] Starting STT service at http://%STT_HOST%:%STT_PORT%  -  Ctrl+C to stop
echo       Model: %STT_MODEL%   (first run downloads the model once)
echo       In the app:  Ai:TranscriptionModel=%STT_MODEL%   Ai:TranscriptionBaseUrl=http://127.0.0.1:%STT_PORT%/v1
echo.

set EXTRA=
if defined STT_API_KEY set EXTRA=--api-key "%STT_API_KEY%"
python stt_server.py --model "%STT_MODEL%" --host "%STT_HOST%" --port "%STT_PORT%" --language "%STT_LANGUAGE%" --preload %EXTRA%
goto end

:err
echo.
echo Something failed. Keep this window open and send the output above.
pause
:end
