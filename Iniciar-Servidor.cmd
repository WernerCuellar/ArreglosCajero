@echo off
setlocal
cd /d "%~dp0"
if exist "Distribucion\Servidor\Servidor.exe" (
 start "" "Distribucion\Servidor\Servidor.exe" --config "%~dp0Configuracion\servidor.local.json"
 exit /b
)
if exist "Codigo\Servidor\bin\Release\net8.0-windows\Servidor.exe" (
 start "" "Codigo\Servidor\bin\Release\net8.0-windows\Servidor.exe" --config "%~dp0Configuracion\servidor.local.json"
 exit /b
)
if exist "Codigo\Servidor\bin\Debug\net8.0-windows\Servidor.exe" (
 start "" "Codigo\Servidor\bin\Debug\net8.0-windows\Servidor.exe" --config "%~dp0Configuracion\servidor.local.json"
 exit /b
)
echo Abra CajeroRed.sln y compile la solucion, o ejecute Publicar.ps1.
pause
