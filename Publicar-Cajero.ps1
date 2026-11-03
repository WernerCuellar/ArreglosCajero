param([string]$Destino=(Join-Path (Split-Path $PSScriptRoot -Parent) 'CajeroRed_Cajero'))
$ErrorActionPreference='Stop'
$destinoFinal=[IO.Path]::GetFullPath($Destino)
if($destinoFinal -eq [IO.Path]::GetFullPath($PSScriptRoot)){throw 'Use una carpeta separada para el paquete del cajero.'}
& (Join-Path $PSScriptRoot 'Publicar.ps1') -Aplicacion Cajero
if($LASTEXITCODE -ne 0){throw 'Falló la publicación del cajero.'}
$origen=Join-Path $PSScriptRoot 'Distribucion\Cajero'
$salida=Join-Path $destinoFinal 'Distribucion\Cajero'
New-Item -ItemType Directory -Path $salida -Force | Out-Null
foreach($archivo in Get-ChildItem -LiteralPath $origen -Force){
 if($archivo.Name -eq 'Logs' -or $archivo.Name -like '*.local.json'){continue}
 if($archivo.Name -eq 'cajero.json' -and (Test-Path -LiteralPath (Join-Path $salida 'cajero.json'))){continue}
 Copy-Item -LiteralPath $archivo.FullName -Destination $salida -Recurse -Force
}
# Conservar dirección, puerto y código locales, actualizando solo la marca genérica.
$rutaEjemplo=Join-Path $salida 'cajero.json'
try {
 $config=Get-Content -LiteralPath $rutaEjemplo -Raw -Encoding UTF8 | ConvertFrom-Json
 if($config.nombreBanco -eq 'Sistema bancario'){$config.nombreBanco='NETGEAR';$config | ConvertTo-Json | Set-Content -LiteralPath $rutaEjemplo -Encoding UTF8}
}catch{Write-Warning 'Se conservó cajero.json: corríjalo desde Configuración de conexión si está dañado.'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Iniciar-Cajero.cmd') -Destination (Join-Path $destinoFinal 'Iniciar-Cajero.cmd') -Force
foreach($auxiliar in @('Iniciar-NETGEAR.ps1','Sincronizar-Cajero.cmd')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $auxiliar) -Destination $destinoFinal -Force}
$documentos=Join-Path $destinoFinal 'Documentacion\09_Entrega'
New-Item -ItemType Directory -Path $documentos -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Documentacion\09_Entrega\CorreccionesCajero_2026-10-31.md') -Destination $documentos -Force
$manuales=Join-Path $destinoFinal 'Documentacion\06_Manuales'
New-Item -ItemType Directory -Path $manuales -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Documentacion\06_Manuales\ManualDelCajero.md') -Destination $manuales -Force
$verificacion=Join-Path $destinoFinal 'Verificacion'
New-Item -ItemType Directory -Path $verificacion -Force | Out-Null
$manifest=Join-Path $verificacion 'MANIFIESTO_SHA256.csv'
Get-ChildItem -LiteralPath $destinoFinal -Recurse -File |
 Where-Object {$_.FullName -ne $manifest -and $_.Name -notlike '*.local.json' -and $_.FullName -notlike '*\Logs\*'} |
 ForEach-Object {[PSCustomObject]@{Archivo=$_.FullName.Substring($destinoFinal.Length+1).Replace('\','/');Bytes=$_.Length;SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}} |
 Export-Csv -LiteralPath $manifest -NoTypeInformation -Encoding UTF8
Write-Host "Cajero actualizado en $destinoFinal. Se conservaron las configuraciones privadas."
