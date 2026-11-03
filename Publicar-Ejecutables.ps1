param([ValidateSet('Cajero','Servidor','Ambos')][string]$Aplicacion='Ambos')
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
 $env:DOTNET_CLI_HOME=Join-Path $PSScriptRoot '.cli'
 $apps=if($Aplicacion -eq 'Ambos'){@('Cajero','Servidor')}else{@($Aplicacion)}
 foreach($app in $apps){
  & dotnet publish "Codigo/$app/$app.csproj" -c Release -r win-x64 --self-contained true -o ".cli/single/$app" --source https://api.nuget.org/v3/index.json --packages .nuget -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
  if($LASTEXITCODE -ne 0){throw "No se pudo generar $app."}
  $destino=Join-Path $PSScriptRoot '.cli/releases/netgear-2.2.0'
  New-Item -ItemType Directory -Path $destino -Force | Out-Null
  Copy-Item -LiteralPath ".cli/single/$app/$app.exe" -Destination (Join-Path $destino ("NETGEAR_"+$app+"_2.2.0.exe")) -Force
 }
}finally{Pop-Location}
