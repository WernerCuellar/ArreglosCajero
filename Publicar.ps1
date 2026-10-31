param([string]$FuenteNuget='https://api.nuget.org/v3/index.json',[ValidateSet('Servidor','Cajero','Ambos')][string]$Aplicacion='Servidor')
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
 $env:DOTNET_CLI_HOME=Join-Path $PSScriptRoot '.cli'
 $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
 $aplicaciones=if($Aplicacion -eq 'Ambos'){@('Servidor','Cajero')}else{@($Aplicacion)}
 foreach($app in $aplicaciones){
  $proyecto="Codigo/$app/$app.csproj"
  & dotnet restore $proyecto -r win-x64 --source $FuenteNuget --packages .nuget --disable-parallel
  if($LASTEXITCODE -ne 0){throw "Falló la restauración de $app."}
  & dotnet publish $proyecto -c Release -r win-x64 --self-contained true --no-restore -o "Distribucion/$app" -p:DebugType=None -p:DebugSymbols=false
  if($LASTEXITCODE -ne 0){throw "Falló la publicación de $app."}
 }
 if($aplicaciones -contains 'Cajero'){Copy-Item -LiteralPath 'Configuracion/cajero.ejemplo.json' -Destination 'Distribucion/Cajero/cajero.json' -Force}
 if($aplicaciones -contains 'Servidor'){Copy-Item -LiteralPath 'Configuracion/servidor.ejemplo.json' -Destination 'Distribucion/Servidor/servidor.json' -Force}
 Write-Host 'Distribución portable completa. Copie la carpeta completa de cada aplicación.'
} finally { Pop-Location }
