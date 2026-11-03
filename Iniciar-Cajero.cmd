@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0Iniciar-NETGEAR.ps1" (
 echo Falta Iniciar-NETGEAR.ps1. Haga git pull o descargue el paquete completo de GitHub Releases.
 pause
 exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Iniciar-NETGEAR.ps1" -Componente Cajero %*
if errorlevel 1 pause
