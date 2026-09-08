@echo off
setlocal
cd /d "%~dp0"
title Totall OCR Service

set "VENVPY=.venv\Scripts\python.exe"

if exist "%VENVPY%" goto venv_ok
echo [1/3] Creating Python virtual environment...
python -m venv .venv
if errorlevel 1 goto err
:venv_ok
call ".venv\Scripts\activate.bat"

echo [2/3] Installing base requirements...
python -m pip install -q --upgrade pip
python -m pip install -q -r requirements.txt
if errorlevel 1 goto err

if defined OCR_ENGINE goto engine_ok
python -c "import easyocr" >nul 2>&1
if errorlevel 1 goto engine_install
set "OCR_ENGINE=easyocr"
goto engine_ok

:engine_install
if defined OCR_SKIP goto engine_ok
echo [2b] Installing neural OCR engine easyocr. This can take a few minutes...
python -m pip install -q -r requirements-easyocr.txt
if errorlevel 1 goto err
set "OCR_ENGINE=easyocr"

:engine_ok
if not defined OCR_HOST set "OCR_HOST=127.0.0.1"
if not defined OCR_PORT set "OCR_PORT=8765"

echo [3/3] Starting OCR service at http://%OCR_HOST%:%OCR_PORT%  -  Ctrl+C to stop
echo       Engine: %OCR_ENGINE%
python -m uvicorn app.main:app --host %OCR_HOST% --port %OCR_PORT%
goto end

:err
echo.
echo Something failed. Keep this window open and send the output above.
pause
:end
