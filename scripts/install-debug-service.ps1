[CmdletBinding()]
param(
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$serviceName = 'KROTZapret'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourceDirectory = Join-Path $repositoryRoot 'src\KROT.Service\bin\x64\Debug\net48'
$sourceServiceExe = Join-Path $sourceDirectory 'KROT.Service.exe'
$sourceRuntimeManifest = Join-Path $sourceDirectory 'runtime\runtime-manifest.json'
$installRoot = Join-Path $env:ProgramFiles 'KROT zapret Dev\service'
$serviceStateDirectory = Join-Path $env:ProgramData 'KROT zapret\service-state'

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

if (-not (Test-Path -LiteralPath $sourceServiceExe -PathType Leaf)) {
    throw "Debug service is not built: $sourceServiceExe"
}

if (-not (Test-Path -LiteralPath $sourceRuntimeManifest -PathType Leaf)) {
    throw "Zapret runtime was not copied to Debug output: $sourceRuntimeManifest"
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $serviceName -Force
        (Get-Service -Name $serviceName).WaitForStatus(
            [ServiceProcess.ServiceControllerStatus]::Stopped,
            [TimeSpan]::FromSeconds(15))
    }
}

$null = New-Item -ItemType Directory -Path $installRoot -Force
$icacls = Join-Path $env:SystemRoot 'System32\icacls.exe'
$aclResult = & $icacls $installRoot `
    /inheritance:r `
    /grant:r `
    '*S-1-5-18:(OI)(CI)F' `
    '*S-1-5-32-544:(OI)(CI)F' `
    '*S-1-5-32-545:(OI)(CI)RX' `
    /Q
if ($LASTEXITCODE -ne 0) {
    throw "Failed to secure debug service directory: $aclResult"
}

Get-ChildItem -LiteralPath $sourceDirectory -Force |
    Copy-Item -Destination $installRoot -Recurse -Force

$childAclResult = & $icacls (Join-Path $installRoot '*') /reset /T /C /Q
if ($LASTEXITCODE -ne 0) {
    throw "Failed to secure copied debug service files: $childAclResult"
}

$null = New-Item -ItemType Directory -Path $serviceStateDirectory -Force
$stateAclResult = & $icacls $serviceStateDirectory `
    /inheritance:r `
    /grant:r `
    '*S-1-5-18:(OI)(CI)F' `
    '*S-1-5-32-544:(OI)(CI)F' `
    '*S-1-5-32-545:(OI)(CI)RX' `
    /Q
if ($LASTEXITCODE -ne 0) {
    throw "Failed to secure KROT service state: $stateAclResult"
}

$serviceStateChildren = Get-ChildItem -LiteralPath $serviceStateDirectory -Force
if ($serviceStateChildren) {
    $stateChildAclResult = & $icacls (Join-Path $serviceStateDirectory '*') `
        /reset /T /C /Q
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to repair KROT service state child permissions: $stateChildAclResult"
    }
}

$serviceExe = Join-Path $installRoot 'KROT.Service.exe'
$runtimeManifest = Join-Path $installRoot 'runtime\runtime-manifest.json'
if (-not (Test-Path -LiteralPath $serviceExe -PathType Leaf) -or
    -not (Test-Path -LiteralPath $runtimeManifest -PathType Leaf)) {
    throw "Debug service staging is incomplete: $installRoot"
}

$binaryPath = '"{0}" --real-runtime' -f $serviceExe
if ($existing) {
    $serviceConfiguration = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
    $changeResult = Invoke-CimMethod `
        -InputObject $serviceConfiguration `
        -MethodName Change `
        -Arguments @{
            PathName = $binaryPath
            StartMode = 'Manual'
            StartName = 'LocalSystem'
        }
    if ($changeResult.ReturnValue -ne 0) {
        throw "Failed to configure $serviceName service. Win32 error: $($changeResult.ReturnValue)"
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

$serviceSddl = 'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPWPDTLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)'
$securityResult = sc.exe sdset $serviceName $serviceSddl
if ($LASTEXITCODE -ne 0) {
    throw "Failed to grant interactive users start/stop access: $securityResult"
}

if (-not $NoStart) {
    Start-Service -Name $serviceName
    (Get-Service -Name $serviceName).WaitForStatus(
        [ServiceProcess.ServiceControllerStatus]::Running,
        [TimeSpan]::FromSeconds(15))
}

Get-CimInstance Win32_Service -Filter "Name='$serviceName'" |
    Select-Object Name, State, StartMode, PathName
