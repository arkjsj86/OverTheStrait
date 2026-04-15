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
set CHECKPOINT=results\%RUN_ID%\HormuzShip\checkpoint.pt

if exist "%CHECKPOINT%" goto :has_checkpoint

if exist "results\%RUN_ID%" (
    echo  Run folder found but no checkpoint saved yet.
    echo  Starting fresh (previous incomplete data will be overwritten).
    echo.
    goto :force
)

goto :fresh

:has_checkpoint
echo  Checkpoint found: %CHECKPOINT%
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
echo  Starting new training...
goto :run_fresh

:force
echo  Overwriting previous data and starting new run...
goto :run_force

:resume
echo  Resuming from last checkpoint...
goto :run_resume

:run_fresh
echo  Press Play in Unity when you see:
echo  "Start training by pressing the Play button"
echo ----------------------------------------
echo.
"%LEARN%" %CONFIG% --run-id=%RUN_ID%
goto :done

:run_force
echo  Press Play in Unity when you see:
echo  "Start training by pressing the Play button"
echo ----------------------------------------
echo.
"%LEARN%" %CONFIG% --run-id=%RUN_ID% --force
goto :done

:run_resume
echo  Press Play in Unity when you see:
echo  "Start training by pressing the Play button"
echo ----------------------------------------
echo.
"%LEARN%" %CONFIG% --run-id=%RUN_ID% --resume
goto :done

:quit
echo  Cancelled.

:done
pause
