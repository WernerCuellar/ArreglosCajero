param([string]$ServidorSql='.\SQLEXPRESS')
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
 $env:CAJERO_SQL_SERVIDOR=$ServidorSql
 & dotnet build CajeroRed.sln -c Debug
 if($LASTEXITCODE -ne 0){throw 'Error de compilación.'}
 $carpeta=Join-Path $PSScriptRoot 'Pruebas/Evidencias'
 New-Item -ItemType Directory -Path $carpeta -Force | Out-Null
 & dotnet Pruebas/bin/Debug/net8.0-windows/Pruebas.dll --integracion 2>&1 | Tee-Object -FilePath (Join-Path $carpeta 'ultima_ejecucion.txt')
 if($LASTEXITCODE -ne 0){throw 'Fallaron las pruebas; consulte la salida.'}
} finally { Pop-Location }
