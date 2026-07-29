[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'KROTZapret'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $service) {
    exit
}

if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
    Stop-Service -Name $serviceName -Force
    (Get-Service -Name $serviceName).WaitForStatus(
        [ServiceProcess.ServiceControllerStatus]::Stopped,
        [TimeSpan]::FromSeconds(15))
}

$deleteResult = sc.exe delete $serviceName
if ($LASTEXITCODE -ne 0) {
    throw "Failed to delete $serviceName service: $deleteResult"
}
