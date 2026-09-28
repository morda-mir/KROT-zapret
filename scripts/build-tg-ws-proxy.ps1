[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\third_party\telegram\tg-ws-proxy-v1.10.4\TgWsProxy_console.exe')
)

$ErrorActionPreference = 'Stop'
$tag = 'v1.10.4'
$commit = '70b982da2ca75637b61f281170e4ed57df763db8'
$workRoot = Join-Path $env:TEMP ('krot-tg-ws-proxy-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $workRoot 'source'
$venvRoot = Join-Path $workRoot 'venv'
$distRoot = Join-Path $workRoot 'dist'
$buildRoot = Join-Path $workRoot 'build'

New-Item -ItemType Directory -Path $workRoot | Out-Null
git clone --branch $tag --depth 1 https://github.com/Flowseal/tg-ws-proxy.git $sourceRoot
if ($LASTEXITCODE -ne 0) {
    throw "Could not clone tg-ws-proxy $tag."
}

$actualCommit = (git -C $sourceRoot rev-parse HEAD).Trim()
if ($actualCommit -ne $commit) {
    throw "Unexpected tg-ws-proxy commit: $actualCommit"
}

python -m venv $venvRoot
$python = Join-Path $venvRoot 'Scripts\python.exe'
& $python -m pip install --disable-pip-version-check `
    pyinstaller==6.16.0 `
    cryptography==46.0.5 `
    certifi==2025.10.5 `
    psutil==7.0.0
if ($LASTEXITCODE -ne 0) {
    throw 'Could not install pinned TG WS Proxy build dependencies.'
}

& $python -m PyInstaller `
    --noconfirm `
    --clean `
    --onefile `
    --console `
    --name TgWsProxy_console `
    --paths $sourceRoot `
    --collect-data certifi `
    --exclude-module PIL `
    --exclude-module tkinter `
    --exclude-module customtkinter `
    --exclude-module pystray `
    --exclude-module pyperclip `
    --distpath $distRoot `
    --workpath $buildRoot `
    --specpath $buildRoot `
    (Join-Path $sourceRoot 'proxy\tg_ws_proxy.py')
if ($LASTEXITCODE -ne 0) {
    throw 'PyInstaller failed to build TG WS Proxy.'
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $distRoot 'TgWsProxy_console.exe') -Destination $OutputPath -Force
$hash = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "TG WS Proxy: $OutputPath"
Write-Host "SHA-256: $hash"
