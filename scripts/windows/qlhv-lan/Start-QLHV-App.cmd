@echo off
setlocal
start "" /b powershell.exe -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0Start-QLHV-App.ps1" -StartupMode ProductionService
endlocal
