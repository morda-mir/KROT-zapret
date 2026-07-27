[CmdletBinding()]
param(
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$serviceName = 'KROTZapret'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$serviceExe = Join-Path $repositoryRoot 'src\KROT.Service\bin\x64\Debug\net48\KROT.Service.exe'
$runtimeManifest = Join-Path $repositoryRoot 'src\KROT.Service\bin\x64\Debug\net48\runtime\runtime-manifest.json'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdministrator) {
    $elevatedArguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $PSCommandPath)
    )
    if ($NoStart) {
        $elevatedArguments += '-NoStart'
    }

    Start-Process powershell.exe -Verb RunAs -ArgumentList $elevatedArguments -Wait
    exit
}

if (-not (Test-Path -LiteralPath $serviceExe -PathType Leaf)) {
    throw "Debug service is not built: $serviceExe"
}

if (-not (Test-Path -LiteralPath $runtimeManifest -PathType Leaf)) {
    throw "Zapret runtime was not copied to Debug output: $runtimeManifest"
}

$binaryPath = '"{0}" --real-runtime' -f $serviceExe
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $serviceName -Force
        (Get-Service -Name $serviceName).WaitForStatus(
            [ServiceProcess.ServiceControllerStatus]::Stopped,
            [TimeSpan]::FromSeconds(15))
    }

    $result = sc.exe config $serviceName binPath= $binaryPath start= demand obj= LocalSystem
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to configure $serviceName service: $result"
    }
}
else {
    New-Service `
        -Name $serviceName `
        -BinaryPathName $binaryPath `
        -DisplayName 'KROT zapret Debug Service' `
        -Description 'Local KROT zapret service for Debug builds.' `
        -StartupType Manual | Out-Null
}

if (-not $NoStart) {
    Start-Service -Name $serviceName
    (Get-Service -Name $serviceName).WaitForStatus(
        [ServiceProcess.ServiceControllerStatus]::Running,
        [TimeSpan]::FromSeconds(15))
}

Get-CimInstance Win32_Service -Filter "Name='$serviceName'" |
    Select-Object Name, State, StartMode, PathName
