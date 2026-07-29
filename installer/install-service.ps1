[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ServiceDirectory
)

$ErrorActionPreference = 'Stop'
$serviceName = 'KROTZapret'
$serviceDirectoryPath = [IO.Path]::GetFullPath($ServiceDirectory)
$serviceExe = Join-Path $serviceDirectoryPath 'KROT.Service.exe'
$runtimeManifest = Join-Path $serviceDirectoryPath 'runtime\runtime-manifest.json'
$serviceStateDirectory = Join-Path $env:ProgramData 'KROT zapret\service-state'

if (-not (Test-Path -LiteralPath $serviceExe -PathType Leaf)) {
    throw "KROT service executable is missing: $serviceExe"
}

if (-not (Test-Path -LiteralPath $runtimeManifest -PathType Leaf)) {
    throw "KROT runtime manifest is missing: $runtimeManifest"
}

$icacls = Join-Path $env:SystemRoot 'System32\icacls.exe'
$serviceAclResult = & $icacls $serviceDirectoryPath `
    /inheritance:r `
    /grant:r `
    '*S-1-5-18:(OI)(CI)F' `
    '*S-1-5-32-544:(OI)(CI)F' `
    '*S-1-5-32-545:(OI)(CI)RX' `
    /T /C /Q
if ($LASTEXITCODE -ne 0) {
    throw "Failed to secure KROT service files: $serviceAclResult"
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

$binaryPath = '"{0}" --real-runtime' -f $serviceExe
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $serviceName -Force
        (Get-Service -Name $serviceName).WaitForStatus(
            [ServiceProcess.ServiceControllerStatus]::Stopped,
            [TimeSpan]::FromSeconds(15))
    }

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
        throw "Failed to update $serviceName service. Win32 error: $($changeResult.ReturnValue)"
    }
}
else {
    New-Service `
        -Name $serviceName `
        -BinaryPathName $binaryPath `
        -DisplayName 'KROT zapret Service' `
        -Description 'Local runtime broker for KROT zapret.' `
        -StartupType Manual | Out-Null
}

$descriptionResult = sc.exe description $serviceName 'Local runtime broker for KROT zapret.'
if ($LASTEXITCODE -ne 0) {
    throw "Failed to set $serviceName description: $descriptionResult"
}

$serviceSddl = 'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPWPDTLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)'
$securityResult = sc.exe sdset $serviceName $serviceSddl
if ($LASTEXITCODE -ne 0) {
    throw "Failed to configure $serviceName permissions: $securityResult"
}
