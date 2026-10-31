param([string]$Servidor='.')
$ErrorActionPreference='Stop'
# Aplicar solo la migración aditiva; no volver a ejecutar 01_CrearBase.sql.
& sqlcmd -S $Servidor -E -C -I -f 65001 -b -i (Join-Path $PSScriptRoot 'BaseDeDatos/Scripts/08_Interbancario.sql')
if($LASTEXITCODE -ne 0){throw 'Falló la migración interbancaria; revise la salida SQL.'}
Write-Host 'Migración aplicada. Inicie el servidor y configure Mi banco NETGEAR.'
