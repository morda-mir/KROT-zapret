[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+(?:\.\d+){0,2}$')]
    [string]$Version = '1.0',

    [string]$InnoSetupCompiler,

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$solutionPath = Join-Path $repositoryRoot 'KROT.sln'
$propsPath = Join-Path $repositoryRoot 'Directory.Build.props'
$installerScript = Join-Path $repositoryRoot 'installer\KROT.iss'
$artifactDirectory = Join-Path $repositoryRoot 'artifacts\installer'
$expectedInstallerName = "KROT-Setup-$Version-x64.exe"
$expectedInstallerPath = Join-Path $artifactDirectory $expectedInstallerName
$checksumPath = "$expectedInstallerPath.sha256"

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Command,

        [Parameter()]
        [string[]]$Arguments = @()
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$Command' exited with code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
    throw "Version file was not found: $propsPath"
}

[xml]$props = Get-Content -LiteralPath $propsPath
$projectVersion = [string]$props.Project.PropertyGroup.Version
if ($projectVersion -ne $Version) {
    throw "Project version '$projectVersion' does not match installer version '$Version'."
}

$issText = Get-Content -LiteralPath $installerScript -Raw
$issVersionMatch = [regex]::Match(
    $issText,
    '(?m)^\s*#define\s+MyAppVersion\s+"(?<version>[^"]+)"')
if (-not $issVersionMatch.Success -or $issVersionMatch.Groups['version'].Value -ne $Version) {
    throw "The default version in installer\KROT.iss does not match '$Version'."
}

if (-not $SkipBuild) {
    Invoke-CheckedCommand dotnet @('restore', $solutionPath)
    Invoke-CheckedCommand dotnet @(
        'build',
        $solutionPath,
        '--configuration', 'Release',
        '--property:Platform=x64',
        '--property:TreatWarningsAsErrors=true',
        '--no-restore'
    )
    Invoke-CheckedCommand dotnet @(
        'test',
        $solutionPath,
        '--configuration', 'Release',
        '--property:Platform=x64',
        '--no-build',
        '--logger', 'trx;LogFileName=release-tests.trx'
    )
    $vulnerabilityReport = & dotnet @(
        'list',
        $solutionPath,
        'package',
        '--vulnerable',
        '--include-transitive',
        '--format', 'json'
    )
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet vulnerability audit exited with code $LASTEXITCODE."
    }
    $vulnerabilityReport | Write-Host
    if (($vulnerabilityReport -join [Environment]::NewLine) -match
        '"vulnerabilities"\s*:') {
        throw 'NuGet vulnerability audit found one or more vulnerable packages.'
    }
}

$appExecutable = Join-Path $repositoryRoot 'src\KROT.App\bin\x64\Release\net48\KROT.exe'
$serviceExecutable = Join-Path $repositoryRoot 'src\KROT.Service\bin\x64\Release\net48\KROT.Service.exe'
$runtimeManifestPath = Join-Path $repositoryRoot 'src\KROT.Service\bin\x64\Release\net48\runtime\runtime-manifest.json'
foreach ($requiredFile in @($appExecutable, $serviceExecutable, $runtimeManifestPath)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required build output was not found: $requiredFile"
    }
}

try {
    $null = Get-Content -LiteralPath $runtimeManifestPath -Raw | ConvertFrom-Json
}
catch {
    throw "Invalid runtime-manifest.json: $($_.Exception.Message)"
}

if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler)) {
    $compilerCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $InnoSetupCompiler = $compilerCandidates |
        Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($InnoSetupCompiler) -or
    -not (Test-Path -LiteralPath $InnoSetupCompiler -PathType Leaf)) {
    throw 'Inno Setup 6 ISCC.exe was not found.'
}

$null = New-Item -ItemType Directory -Path $artifactDirectory -Force
foreach ($oldArtifact in @($expectedInstallerPath, $checksumPath)) {
    if (Test-Path -LiteralPath $oldArtifact -PathType Leaf) {
        Remove-Item -LiteralPath $oldArtifact -Force
    }
}

Invoke-CheckedCommand $InnoSetupCompiler @(
    "/DMyAppVersion=$Version",
    "/DMyOutputDir=$artifactDirectory",
    $installerScript
)

if (-not (Test-Path -LiteralPath $expectedInstallerPath -PathType Leaf)) {
    throw "Inno Setup did not create the expected file: $expectedInstallerPath"
}

$installerHash = (Get-FileHash -LiteralPath $expectedInstallerPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$installerHash  $expectedInstallerName" |
    Set-Content -LiteralPath $checksumPath -Encoding ascii

Write-Host ''
Write-Host "Installer: $expectedInstallerPath"
Write-Host "SHA-256: $installerHash"
