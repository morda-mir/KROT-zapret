[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot 'KROT.sln'

dotnet restore $solutionPath
dotnet build $solutionPath --configuration $Configuration --property:Platform=x64 --no-restore
dotnet test $solutionPath --configuration $Configuration --property:Platform=x64 --no-build

