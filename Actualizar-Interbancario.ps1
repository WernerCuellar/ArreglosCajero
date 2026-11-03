param([string]$Servidor='.')
$ErrorActionPreference='Stop'
# Aplicar solo la migración aditiva; no volver a ejecutar 01_CrearBase.sql.
foreach($nombre in @('08_Interbancario.sql','09_CompatibilidadCajero.sql','10_TransferenciasCajero.sql','11_MonitoreoIndependiente.sql')){
 & sqlcmd -S $Servidor -E -C -I -f 65001 -b -i (Join-Path $PSScriptRoot ('BaseDeDatos/Scripts/'+$nombre))
 if($LASTEXITCODE -ne 0){throw "Falló $nombre."}
}
if($LASTEXITCODE -ne 0){throw 'Falló la migración interbancaria; revise la salida SQL.'}
Write-Host 'Migraciones 08/09/10/11 aplicadas. Inicie el servidor y revise Mi banco NETGEAR.'
