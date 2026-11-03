[CmdletBinding(PositionalBinding=$false)]
param([Parameter(Mandatory=$true)][ValidateSet('Cajero','Servidor')][string]$Componente,
 [Parameter(Position=0)][string]$Codigo='', [Parameter(Position=1)][string]$Direccion='',
 [Parameter(Position=2)][string]$Puerto='', [switch]$Actualizar)
$ErrorActionPreference='Stop'
try {
 $version='2.2.0';$repositorio='https://github.com/WernerCuellar/ArreglosCajero'
 $nombre='NETGEAR_'+$Componente+'_'+$version+'.exe'
 $carpeta=Join-Path $PSScriptRoot ("Aplicaciones\"+$Componente)
 try{New-Item -ItemType Directory -Path $carpeta -Force | Out-Null}
 catch{$carpeta=Join-Path $env:LOCALAPPDATA ("NETGEAR\Aplicaciones\"+$Componente);New-Item -ItemType Directory -Path $carpeta -Force | Out-Null}
 $exe=Join-Path $carpeta $nombre;$portable=Join-Path $PSScriptRoot ("Distribucion\"+$Componente+"\"+$Componente+".exe")
 function Version-Correcta([string]$ruta) {
  if(!(Test-Path -LiteralPath $ruta)){return $false}
  return (Get-Item -LiteralPath $ruta).VersionInfo.ProductVersion.StartsWith($version)
 }
 if(!$Actualizar -and (Version-Correcta $portable)){$exe=$portable}
 elseif($Actualizar -or !(Version-Correcta $exe)){
  Write-Host "Descargando $Componente NETGEAR $version. No requiere .NET ni Visual Studio."
  [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12
  $base="$repositorio/releases/download/netgear-$version";$temporal=$exe+'.download'
  Invoke-WebRequest -UseBasicParsing -Uri "$base/$nombre" -OutFile $temporal
  $sumas=(Invoke-WebRequest -UseBasicParsing -Uri "$base/SHA256SUMS.txt").Content
  $linea=($sumas -split '\r?\n' | Where-Object {$_ -match ("^[A-Fa-f0-9]{64}\s+"+[regex]::Escape($nombre)+"\s*$")} | Select-Object -First 1)
  if(!$linea){throw 'No se encontró SHA256 del ejecutable.'}
  if((Get-FileHash -LiteralPath $temporal -Algorithm SHA256).Hash -ne ($linea.Trim() -split '\s+')[0]){throw 'La descarga no coincide con SHA256.'}
  Move-Item -LiteralPath $temporal -Destination $exe -Force
 }
 if(!(Version-Correcta $exe)){throw 'No se encontró una aplicación compatible.'}
 $argumentos=@()
 if($Componente -eq 'Cajero'){
  if($Codigo){if($Codigo -notmatch '^[A-Za-z0-9_-]{1,24}$'){throw 'Código inválido.'};$argumentos+=@('--cajero',$Codigo)}
  if($Direccion){if([Uri]::CheckHostName($Direccion) -eq [UriHostNameType]::Unknown){throw 'Dirección inválida.'};$argumentos+=@('--servidor',$Direccion)}
  if($Puerto){$numero=0;if(![int]::TryParse($Puerto,[ref]$numero) -or $numero -lt 1 -or $numero -gt 65535){throw 'Puerto inválido.'};$argumentos+=@('--puerto',$numero.ToString())}
 }else{
  $configuracion=Join-Path $PSScriptRoot 'Configuracion';New-Item -ItemType Directory -Path $configuracion -Force | Out-Null
  $argumentos=@('--config',('"'+(Join-Path $configuracion 'servidor.local.json')+'"'))
 }
 $parametros=@{FilePath=$exe;WorkingDirectory=(Split-Path $exe -Parent);WindowStyle='Normal';PassThru=$true}
 if($argumentos.Count -gt 0){$parametros.ArgumentList=$argumentos}
 $proceso=Start-Process @parametros;Start-Sleep -Seconds 2
 if($proceso.HasExited -and $proceso.ExitCode -ne 0){throw "La aplicación terminó con código $($proceso.ExitCode). Descargue el ZIP completo o revise Windows."}
 Write-Host "$Componente NETGEAR $version iniciado."
}catch{
 Write-Host ("No se pudo iniciar NETGEAR: "+$_.Exception.Message) -ForegroundColor Red
 Write-Host "Descargas: https://github.com/WernerCuellar/ArreglosCajero/releases"
 Write-Host 'Extraiga el ZIP completo o use el ejecutable único. Cierre el cajero antes de actualizar.'
 exit 1
}
