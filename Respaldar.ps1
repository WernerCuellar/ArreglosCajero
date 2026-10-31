param([string]$Servidor='.\SQLEXPRESS',[string]$CuentaServicio='NT SERVICE\MSSQL$SQLEXPRESS')
$ErrorActionPreference='Stop'
$sqlcmd=(Get-Command sqlcmd -ErrorAction Stop).Source
$destino=Join-Path $PSScriptRoot 'BaseDeDatos\Respaldos'
New-Item -ItemType Directory -Path $destino -Force | Out-Null
# El servicio SQL necesita escribir el .bak y el usuario debe poder leerlo.
# Solo se concede Modify a la cuenta del servicio en esta carpeta de respaldos.
$acl=Get-Acl -LiteralPath $destino
$regla=[System.Security.AccessControl.FileSystemAccessRule]::new($CuentaServicio,'Modify','ContainerInherit, ObjectInherit','None','Allow')
$acl.SetAccessRule($regla)
Set-Acl -LiteralPath $destino -AclObject $acl
$ruta=Join-Path $destino 'BD_CajeroRed_Fase2.bak'
$sqlRuta=$ruta.Replace("'","''")
& $sqlcmd -S $Servidor -E -C -I -b -Q "BACKUP DATABASE BD_CajeroRed_Fase2 TO DISK=N'$sqlRuta' WITH COPY_ONLY,INIT,CHECKSUM; RESTORE VERIFYONLY FROM DISK=N'$sqlRuta' WITH CHECKSUM;"
if($LASTEXITCODE -ne 0){throw 'El respaldo o su verificación falló.'}
Get-FileHash -LiteralPath (Join-Path $destino 'BD_CajeroRed_Fase2.bak') -Algorithm SHA256 | Format-List | Out-File -LiteralPath (Join-Path $destino 'SHA256.txt') -Encoding utf8
Write-Host 'Respaldo COPY_ONLY creado y verificado en BaseDeDatos/Respaldos.'
