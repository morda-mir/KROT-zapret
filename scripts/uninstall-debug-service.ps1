[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'KROTZapret'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdministrator) {
    Start-Process powershell.exe `
        -Verb RunAs `
        -ArgumentList @(
            '-NoProfile',
            '-ExecutionPolicy', 'Bypass',
            '-File', ('"{0}"' -f $PSCommandPath)
        ) `
        -Wait
    exit
}

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Output "$serviceName is not installed."
    exit
}

if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
    Stop-Service -Name $serviceName -Force
    (Get-Service -Name $serviceName).WaitForStatus(
        [ServiceProcess.ServiceControllerStatus]::Stopped,
        [TimeSpan]::FromSeconds(15))
}

$result = sc.exe delete $serviceName
if ($LASTEXITCODE -ne 0) {
    throw "Failed to delete $serviceName service: $result"
}

Write-Output "$serviceName was removed."
