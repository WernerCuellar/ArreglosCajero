param([string]$Servidor='.\SQLEXPRESS')
$ErrorActionPreference='Stop'
$sqlcmd=(Get-Command sqlcmd -ErrorAction Stop).Source
$scripts=Join-Path $PSScriptRoot 'BaseDeDatos\Scripts'
if(!(Test-Path -LiteralPath $scripts)){throw 'No se encontró la carpeta de scripts.'}
foreach($archivo in Get-ChildItem -LiteralPath $scripts -Filter '*.sql' | Sort-Object Name){
 Write-Host "Aplicando $($archivo.Name)..."
 & $sqlcmd -S $Servidor -E -C -I -f 65001 -b -r 1 -i $archivo.FullName
 if($LASTEXITCODE -ne 0){throw "Falló $($archivo.Name). No continúe sin revisar el error."}
}
Write-Host 'Base lista. Inicie el servidor y realice la inicialización diaria de cada cajero.'
