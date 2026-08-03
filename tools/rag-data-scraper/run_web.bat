@echo off
title DigitalOps RAG Data Scraper Web Dashboard
echo ===================================================
echo   DigitalOps - RAG Data Scraper Web Dashboard
echo ===================================================
echo.

cd /d "%~dp0"

set "PYTHON_CMD="
if exist ".venv\Scripts\python.exe" set "PYTHON_CMD=.venv\Scripts\python.exe"
if not defined PYTHON_CMD if exist "..\..\.venv\Scripts\python.exe" set "PYTHON_CMD=..\..\.venv\Scripts\python.exe"
if not defined PYTHON_CMD set "PYTHON_CMD=python"

echo Using Python path: %PYTHON_CMD%
echo.
echo Starting Web Dashboard on http://localhost:8000 ...
"%PYTHON_CMD%" -m rag_data_scraper.cli web --open

if errorlevel 1 (
    echo.
    echo ===================================================
    echo Processing finished with code %errorlevel%.
    echo ===================================================
)

pause
