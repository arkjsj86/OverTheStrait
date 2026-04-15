@echo off
cd /d "%~dp0"

echo.
echo ========================================
echo  Hormuz ML-Agents Training Launcher
echo ========================================
echo.
echo  Config : config/hormuz_stage1.yaml
echo  Run ID : hormuz_run1
echo  GPU    : RTX 3070 Ti (CUDA)
echo.

set LEARN=venv_mlagents\Scripts\mlagents-learn.exe
set CONFIG=config/hormuz_stage1.yaml
set RUN_ID=hormuz_run1
set RESULTS=results\%RUN_ID%

if not exist "%RESULTS%" goto :fresh

echo  Previous training data found: %RESULTS%
echo.
echo  [R] Resume   - continue from last checkpoint
echo  [N] New run  - delete previous data and restart
echo  [Q] Quit
echo.
set /p CHOICE="  Select (R/N/Q): "

if /i "%CHOICE%"=="R" goto :resume
if /i "%CHOICE%"=="N" goto :force
if /i "%CHOICE%"=="Q" goto :quit
echo  Invalid input. Exiting.
goto :quit

:fresh
echo  No previous data found. Starting new training...
echo  Press Play in Unity when you see:
echo  "Start training by pressing the Play button"
echo ----------------------------------------
echo.
"%LEARN%" %CONFIG% --run-id=%RUN_ID%
goto :done

:resume
echo  Resuming from last checkpoint...
echo  Press Play in Unity when you see:
echo  "Start training by pressing the Play button"
echo ----------------------------------------
echo.
"%LEARN%" %CONFIG% --run-id=%RUN_ID% --resume
goto :done

:force
echo  Starting new run (previous data will be overwritten)...
echo  Press Play in Unity when you see:
echo  "Start training by pressing the Play button"
echo ----------------------------------------
echo.
"%LEARN%" %CONFIG% --run-id=%RUN_ID% --force
goto :done

:quit
echo  Cancelled.

:done
pause
