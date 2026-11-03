#requires -RunAsAdministrator
param([ValidateRange(1,65535)][int]$Puerto=5000,[string[]]$RedPermitida=@('LocalSubnet'),[string]$Programa='')
$ErrorActionPreference='Stop'
if(!$Programa){$Programa=Join-Path $PSScriptRoot 'Distribucion\Servidor\Servidor.exe'}
$programa=[IO.Path]::GetFullPath($Programa)
if(!(Test-Path -LiteralPath $programa)){
 $programa=Join-Path $PSScriptRoot 'Aplicaciones\Servidor\NETGEAR_Servidor_2.2.1.exe'
 if(!(Test-Path -LiteralPath $programa)){throw 'Inicie una vez Iniciar-Servidor.cmd o indique -Programa con la ruta del EXE.'}
}
foreach($regla in @(@{Nombre="NETGEAR-Servidor-TCP-$Puerto";Protocolo='TCP';Puerto=$Puerto},@{Nombre='NETGEAR-Discovery-UDP-5005';Protocolo='UDP';Puerto=5005})){
 $existente=Get-NetFirewallRule -Name $regla.Nombre -ErrorAction SilentlyContinue
 if($existente){
  Set-NetFirewallRule -Name $regla.Nombre -Enabled True -Profile Any -Direction Inbound -Action Allow -Program $programa -RemoteAddress $RedPermitida | Out-Null
  $existente | Get-NetFirewallPortFilter | Set-NetFirewallPortFilter -Protocol $regla.Protocolo -LocalPort $regla.Puerto | Out-Null
 }else{
  New-NetFirewallRule -Name $regla.Nombre -DisplayName $regla.Nombre -Direction Inbound -Action Allow -Enabled True -Protocol $regla.Protocolo -LocalPort $regla.Puerto -Program $programa -Profile Any -RemoteAddress $RedPermitida | Out-Null
 }
}
Write-Host "Reglas NETGEAR: TCP $Puerto y UDP 5005 para $programa; origen permitido: $($RedPermitida -join ', ')."
