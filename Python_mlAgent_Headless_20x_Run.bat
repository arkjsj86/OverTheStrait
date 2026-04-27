@echo off
cd /d "%~dp0"

echo.
echo ========================================
echo  Hormuz Headless 20x Training Launcher
echo ========================================
echo.

set LEARN=venv_mlagents\Scripts\mlagents-learn.exe
set CONFIG=config/hormuz_stage1_headless_20x.yaml
set RUN_ID=hormuz_headless20x
set BUILD_DIR=Builds\WindowsHeadless
set ENV_PATH=

for %%F in ("%BUILD_DIR%\*.exe") do (
    set ENV_PATH=%%~fF
    goto :found_build
)

echo  No Unity build found in %BUILD_DIR%
echo  Put a Windows build here, for example:
echo  %CD%\Builds\WindowsHeadless\OverTheStrait.exe
echo.
pause
goto :eof

:found_build
echo  Build : %ENV_PATH%
echo  Config: %CONFIG%
echo  Run ID: %RUN_ID%
echo  Mode  : Build + no_graphics + 20x
echo.

set CHECKPOINT=results\%RUN_ID%\HormuzShip\checkpoint.pt

if exist "%CHECKPOINT%" goto :has_checkpoint

if exist "results\%RUN_ID%" (
    echo  Run folder found but no checkpoint saved yet.
    echo  Starting fresh ^(previous incomplete data will be overwritten^).
    echo(
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
echo  Starting new headless training...
goto :run_fresh

:force
echo  Overwriting previous data and starting new headless run...
goto :run_force

:resume
echo  Resuming headless training...
goto :run_resume

:run_fresh
start "OverTheStrait Monitor" "%~dp0Training_Status_Monitor.bat" %RUN_ID%
echo.
"%LEARN%" %CONFIG% --env="%ENV_PATH%" --run-id=%RUN_ID%
goto :done

:run_force
start "OverTheStrait Monitor" "%~dp0Training_Status_Monitor.bat" %RUN_ID%
echo.
"%LEARN%" %CONFIG% --env="%ENV_PATH%" --run-id=%RUN_ID% --force
goto :done

:run_resume
start "OverTheStrait Monitor" "%~dp0Training_Status_Monitor.bat" %RUN_ID%
echo.
"%LEARN%" %CONFIG% --env="%ENV_PATH%" --run-id=%RUN_ID% --resume
goto :done

:quit
echo  Cancelled.

:done
pause
