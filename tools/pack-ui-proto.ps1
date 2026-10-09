<#
.SYNOPSIS
    Packs a clean v2crackN build for Windows 10/11.

.DESCRIPTION
    1. asks for the version (or takes -Version) and builds the archive name from it;
    2. wipes local logs / temp / test configs of the dev payload (guiLogs, guiTemps,
       binConfigs, guiBackups) so nothing personal gets packed;
    3. dotnet publish -> single self-contained win-x64 exe (no .NET on the target PC);
    4. copies xray/geo/wintun/dpi into bin\;
    5. zips it all into dist\v2crackN-<version>-windows-11.zip + .sha256;
    6. verifies that no personal file (configs, DB, subscriptions, logs) got in.

    guiConfigs\ (your guiNConfig.json + guiNDB.db with subscriptions) is NEVER deleted
    from this machine and is NEVER packed - the archive ships empty folders only.

.EXAMPLE
    .\tools\pack-ui-proto.ps1              # asks for the version
    .\tools\pack-ui-proto.ps1 -Version 1.2.3
#>
[CmdletBinding()]
param(
    # version for the archive name; when omitted the script asks for it
    [string]$Version,
    [string]$CoreSource = "$env:APPDATA\v2crackNG-Win\core",
    [string]$SingBoxExe = "C:\Program Files\FlyFrogLLC\Happ\tun\sing-box.exe",
    [switch]$Installer
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tools\ui-proto\UiProto.csproj'
$uiDir = Join-Path $root 'tools\ui-proto'
$publishDir = Join-Path $uiDir 'publish'
$stage = Join-Path $env:TEMP 'v2crackN-pack\app'
$dist = Join-Path $root 'dist'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

# --------------------------------------------------------------- version
while ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Read-Host 'Версия (например 1.2.3)').Trim()
}
$Version = ($Version -replace '[\\/:*?"<>|]', '-').Trim('-', ' ')
if ($Version.Length -eq 0) { throw 'пустая версия' }

$OutputName = "v2crackN-$Version-windows-10-11.zip"
$zip = Join-Path $dist $OutputName
$installerName = "v2crackN-$Version-windows-10-11-installer.exe"
$installerPath = Join-Path $dist $installerName

function Require-File([string]$Path, [string]$Label) {
    if (-not (Test-Path $Path -PathType Leaf)) {
        throw "Missing ${Label}: $Path"
    }
}

function Remove-StaleJunction([string]$Path) {
    if (-not (Test-Path $Path)) { return }
    $item = Get-Item $Path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        Write-Host "Removing stale junction: $Path"
        cmd /c "rmdir `"$Path`"" | Out-Null
    }
}

Require-File $project 'UI project'
Require-File (Join-Path $CoreSource 'xray.exe') 'Xray core'
Require-File (Join-Path $CoreSource 'geoip.dat') 'geoip.dat'
Require-File (Join-Path $CoreSource 'geosite.dat') 'geosite.dat'
Require-File (Join-Path $CoreSource 'wintun.dll') 'wintun.dll'
Require-File (Join-Path $root 'tools\dpi\ciadpi.exe') 'DPI module'

# A previous local payload may contain a junction to a deleted main-app build.
Remove-StaleJunction (Join-Path $publishDir 'bin')

# ------------------------------------------------- clean logs & temp state
# только логи/временное: guiConfigs (твой DB и подписки) не трогаем
Write-Host '== clean local logs/temp ==' -ForegroundColor Cyan
foreach ($dir in 'guiLogs', 'guiTemps', 'binConfigs', 'guiBackups') {
    $p = Join-Path $publishDir $dir
    if (Test-Path $p) {
        Get-ChildItem $p -Force -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }
}
foreach ($file in 'payload.json', 'payload-state.json', 'proto-settings.json') {
    $p = Join-Path $publishDir $file
    if (Test-Path $p) { Remove-Item $p -Force -ErrorAction SilentlyContinue }
}

# ---------------------------------------------------------------- publish
$env:HTTP_PROXY = $null
$env:HTTPS_PROXY = $null
if (Test-Path (Split-Path $stage -Parent)) {
    Remove-Item (Split-Path $stage -Parent) -Recurse -Force
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Write-Host "== publish self-contained win-x64 (v$Version) ==" -ForegroundColor Cyan
& $dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -o $stage -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

Get-ChildItem $stage -Filter '*.pdb' -File -ErrorAction SilentlyContinue | Remove-Item -Force
$bin = Join-Path $stage 'bin'
New-Item -ItemType Directory -Path (Join-Path $bin 'xray'), (Join-Path $bin 'dpi') -Force | Out-Null

Write-Host '== copy runtime core ==' -ForegroundColor Cyan
Copy-Item (Join-Path $CoreSource 'xray.exe') (Join-Path $bin 'xray\xray.exe') -Force
Copy-Item (Join-Path $CoreSource 'geoip.dat') (Join-Path $bin 'geoip.dat') -Force
Copy-Item (Join-Path $CoreSource 'geosite.dat') (Join-Path $bin 'geosite.dat') -Force
Copy-Item (Join-Path $CoreSource 'wintun.dll') (Join-Path $bin 'wintun.dll') -Force
Copy-Item (Join-Path $root 'tools\dpi\ciadpi.exe') (Join-Path $bin 'dpi\ciadpi.exe') -Force

if (Test-Path $SingBoxExe -PathType Leaf) {
    New-Item -ItemType Directory -Path (Join-Path $bin 'sing_box') -Force | Out-Null
    Copy-Item $SingBoxExe (Join-Path $bin 'sing_box\sing-box.exe') -Force
    Write-Host 'sing-box: included'
}
else {
    throw "sing-box is required for the release package: $SingBoxExe"
}

# Runtime folders are created by the app; ship them empty - no private state.
foreach ($name in 'guiConfigs', 'guiLogs', 'binConfigs', 'guiBackups', 'guiTemps') {
    New-Item -ItemType Directory -Path (Join-Path $stage $name) -Force | Out-Null
}

@'
v2crackN for Windows 10/11

Extract the archive and run v2crackN.exe.
The package is self-contained and does not require a separate .NET installation.
Keep the bin folder next to the executable.
'@ | Set-Content (Join-Path $stage 'README.txt') -Encoding ASCII

# ------------------------------------------------- no personal data allowed
$private = Get-ChildItem $stage -Recurse -File -Force |
    Where-Object {
        $_.Name -match '^(guiNConfig|guiNDB|payload|proto-settings)' -or
        $_.Extension -in '.log', '.db' -or
        $_.FullName -match '[\\/](guiLogs|guiBackups|guiTemps)[\\/]'
    }
if ($private) {
    $private | ForEach-Object { Write-Host "PERSONAL: $($_.FullName)" -ForegroundColor Red }
    throw 'в архив попали личные файлы — упаковка отменена'
}

# ---------------------------------------------------------------- archive
Write-Host '== archive ==' -ForegroundColor Cyan
New-Item -ItemType Directory -Path $dist -Force | Out-Null
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$zip.sha256", "$hash *$OutputName`n")
$size = [Math]::Round((Get-Item $zip).Length / 1MB, 2)

if ($Installer) {
    Write-Host '== installer ==' -ForegroundColor Cyan
    $installerProject = Join-Path $root 'tools\installer\Installer.csproj'
    $installerStage = Join-Path $env:TEMP 'v2crackN-pack\installer'
    if (Test-Path $installerStage) { Remove-Item $installerStage -Recurse -Force }
    New-Item -ItemType Directory -Path $installerStage -Force | Out-Null
    $installerPath = Join-Path $dist $installerName
    & $dotnet publish $installerProject -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        "-p:ZipPath=$zip" "-p:Version=$Version" -o $installerStage -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw "installer publish failed: $LASTEXITCODE" }
    $builtInstaller = Join-Path $installerStage 'v2crackN-Installer.exe'
    Move-Item $builtInstaller $installerPath -Force
    $installerHash = (Get-FileHash $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$installerPath.sha256", "$installerHash *$installerName`n")
    $installerSize = [Math]::Round((Get-Item $installerPath).Length / 1MB, 2)
    
    }

Write-Host ''
Write-Host "VERSION: $Version" -ForegroundColor Green
Write-Host "READY:   $zip" -ForegroundColor Green
Write-Host "SIZE:    $size MB"
Write-Host "SHA256:  $hash"
if ($Installer) {
    Write-Host "INSTALLER: $installerPath" -ForegroundColor Green
    Write-Host "SIZE:      $installerSize MB"
    Write-Host "SHA256:    $installerHash"
}
