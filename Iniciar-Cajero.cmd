@echo off
setlocal
cd /d "%~dp0"
set "codigo=%~1"
set "direccion=%~2"
set "puerto=%~3"
set "aplicacion="
if exist "Distribucion\Cajero\Cajero.exe" (
 set "aplicacion=Distribucion\Cajero\Cajero.exe"
 goto iniciar
)
if exist "Codigo\Cajero\bin\Release\net8.0-windows\Cajero.exe" (
 set "aplicacion=Codigo\Cajero\bin\Release\net8.0-windows\Cajero.exe"
 goto iniciar
)
echo No se encuentra Cajero.exe. Extraiga el paquete completo o ejecute Publicar.ps1 -Aplicacion Cajero.
pause
exit /b 1
:iniciar
if "%codigo%"=="" if "%direccion%"=="" if "%puerto%"=="" (
 start "" "%aplicacion%"
 exit /b
)
if "%codigo%"=="" set "codigo=ATM-01"
if "%puerto%"=="" goto sin_puerto
if "%direccion%"=="" (
 start "" "%aplicacion%" --cajero "%codigo%" --puerto "%puerto%"
) else (
 start "" "%aplicacion%" --cajero "%codigo%" --servidor "%direccion%" --puerto "%puerto%"
)
exit /b
:sin_puerto
if "%direccion%"=="" (
 start "" "%aplicacion%" --cajero "%codigo%"
) else (
 start "" "%aplicacion%" --cajero "%codigo%" --servidor "%direccion%"
)
