@echo off
cd /d "%~dp0"

echo.
echo ========================================
echo  Hormuz ML-Agents Training Launcher
echo ========================================
echo.
echo  Config  : config/hormuz_stage1.yaml
echo  Run ID  : hormuz_run1
echo  GPU     : RTX 3070 Ti (CUDA)
echo.
echo  Press Play in Unity Editor when you see:
echo  "Start training by pressing the Play button"
echo.
echo ----------------------------------------

venv_mlagents\Scripts\mlagents-learn.exe config/hormuz_stage1.yaml --run-id=hormuz_run1

REM To resume a previous run, replace the line above with:
REM venv_mlagents\Scripts\mlagents-learn.exe config/hormuz_stage1.yaml --run-id=hormuz_run1 --resume

pause
