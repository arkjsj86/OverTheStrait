@echo off
cd /d "%~dp0"

set PYTHON_EXE=venv_mlagents\Scripts\python.exe
set RUN_ID=%~1

if "%RUN_ID%"=="" set RUN_ID=hormuz_run1

if exist "%PYTHON_EXE%" goto :run

set PYTHON_EXE=python

:run
echo.
echo ========================================
echo  OverTheStrait Training Status Monitor
echo ========================================
echo.
echo  Run ID : %RUN_ID%
echo  Press Ctrl+C to stop.
echo.

"%PYTHON_EXE%" tools\monitor_training_status.py --run-id=%RUN_ID%
