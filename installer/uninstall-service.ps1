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

$deadline = [DateTime]::UtcNow.AddSeconds(15)
while ((Get-Service -Name $serviceName -ErrorAction SilentlyContinue) -and
       [DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 200
}

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    throw "Timed out while deleting $serviceName service."
}
