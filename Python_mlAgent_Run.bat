@echo off
chcp 65001 > nul
cd /d "%~dp0"

echo.
echo ========================================
echo  Hormuz ML-Agents Training Launcher
echo ========================================
echo.

set VENV=%~dp0venv_mlagents\Scripts\mlagents-learn.exe

if not exist "%VENV%" (
    echo [ERROR] venv_mlagents 가상환경을 찾을 수 없습니다.
    echo         D:\Project\OverTheStrait\venv_mlagents 경로를 확인하세요.
    pause
    exit /b 1
)

set RUN_ID=hormuz_run1
set CONFIG=config/hormuz_stage1.yaml

echo  Config  : %CONFIG%
echo  Run ID  : %RUN_ID%
echo  GPU     : RTX 3070 Ti (CUDA)
echo.
echo  학습을 재개하려면 --resume 옵션을 추가하세요.
echo  (이 파일을 텍스트 편집기로 열어 --resume 주석 해제)
echo.
echo ----------------------------------------
echo  트레이너가 준비되면 Unity 에서 Play 를 누르세요.
echo ----------------------------------------
echo.

"%VENV%" %CONFIG% --run-id=%RUN_ID%
REM "%VENV%" %CONFIG% --run-id=%RUN_ID% --resume

pause
