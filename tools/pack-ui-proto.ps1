<#
.SYNOPSIS
    Packs a clean v2crackN build for Windows 10/11.

.DESCRIPTION
    1. asks for the version (or takes -Version) and builds the archive name from it;
    2. creates a fresh staging directory without modifying the developer's data;
    3. dotnet publish -> single self-contained win-x64 exe (no .NET on the target PC);
    4. copies xray/geo/wintun/dpi into bin\;
    5. zips it all into dist\v2crackN-<version>-windows-10-11.zip + .sha256;
    6. verifies that no personal file (configs, DB, subscriptions, logs) got in.

    guiConfigs\ (your guiNConfig.json + guiNDB.db with subscriptions) is NEVER deleted
    from this machine and is NEVER packed. Runtime folders are created by the app.

.EXAMPLE
    .\tools\pack-ui-proto.ps1 -Version 1.2.3 -CoreSource C:\verified\xray -SingBoxExe C:\verified\sing-box\sing-box.exe -Installer
#>
[CmdletBinding()]
param(
    # version for the archive name; when omitted the script asks for it
    [string]$Version,
    [string]$CoreSource,
    [string]$SingBoxExe,
    [switch]$Installer
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'app\UiProto.csproj'
$uiDir = Join-Path $root 'app'
$publishDir = Join-Path $uiDir 'publish'
$workDir = Join-Path $env:TEMP ('v2crackN-pack-' + [guid]::NewGuid().ToString('N'))
$stage = Join-Path $workDir 'app'
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

# Never overwrite an existing release artifact, even when a previous build failed.
$outputs = @($zip, "$zip.sha256")
if ($Installer) { $outputs += @($installerPath, "$installerPath.sha256") }
foreach ($output in $outputs) {
    if (Test-Path -LiteralPath $output) { throw "Output already exists: $output. Choose another version." }
}

function Require-File([string]$Path, [string]$Label) {
    if (-not (Test-Path $Path -PathType Leaf)) {
        throw "Missing ${Label}: $Path"
    }
}

Require-File $project 'UI project'
if ([string]::IsNullOrWhiteSpace($CoreSource)) { throw 'Specify -CoreSource with files from a verified official Xray release.' }
Require-File (Join-Path $CoreSource 'xray.exe') 'Xray core'
Require-File (Join-Path $CoreSource 'geoip.dat') 'geoip.dat'
Require-File (Join-Path $CoreSource 'geosite.dat') 'geosite.dat'
Require-File (Join-Path $CoreSource 'wintun.dll') 'wintun.dll'
Require-File (Join-Path $root 'tools\dpi\ciadpi.exe') 'DPI module'
if ([string]::IsNullOrWhiteSpace($SingBoxExe)) { throw 'Specify -SingBoxExe with a verified official sing-box release.' }
Require-File $SingBoxExe 'sing-box core'

# The release is built in a fresh staging directory. Never clean the developer's
# publish directory: it can contain settings, logs, backups, or junctions.

# Keep the exact staging path for manual cleanup; never delete an unverified path.
Write-Host "Temporary build files: $workDir"
# ---------------------------------------------------------------- publish
$env:HTTP_PROXY = $null
$env:HTTPS_PROXY = $null
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

New-Item -ItemType Directory -Path (Join-Path $bin 'sing_box') -Force | Out-Null
Copy-Item -LiteralPath $SingBoxExe -Destination (Join-Path $bin 'sing_box\sing-box.exe') -Force
Write-Host 'sing-box: included'

# Runtime data directories are created by the application, not shipped.

@'
v2crackN for Windows 10/11

Extract the archive and run v2crackN.exe.
The package is self-contained and does not require a separate .NET installation.
Keep the bin folder next to the executable.
'@ | Set-Content (Join-Path $stage 'README.txt') -Encoding ASCII

# Allow only explicitly enumerated release files; never package unexpected data.
$allowed = @('v2crackN.exe', 'README.txt', 'bin\xray\xray.exe', 'bin\sing_box\sing-box.exe',
    'bin\geoip.dat', 'bin\geosite.dat', 'bin\wintun.dll', 'bin\dpi\ciadpi.exe')
$actual = @(Get-ChildItem -LiteralPath $stage -Recurse -File -Force | ForEach-Object {
    $_.FullName.Substring($stage.Length).TrimStart([char[]]@('\', '/'))
})
foreach ($file in $actual) {
    if ($allowed -notcontains $file) { throw "Unexpected file in package: $file" }
}
foreach ($file in $allowed) {
    if ($actual -notcontains $file) { throw "Missing file in package: $file" }
}

# ---------------------------------------------------------------- archive
Write-Host '== archive ==' -ForegroundColor Cyan
New-Item -ItemType Directory -Path $dist -Force | Out-Null
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$zip.sha256", "$hash *$OutputName`n")
$size = [Math]::Round((Get-Item $zip).Length / 1MB, 2)

if ($Installer) {
    Write-Host '== installer ==' -ForegroundColor Cyan
    $installerProject = Join-Path $root 'tools\installer\Installer.csproj'
    $installerStage = Join-Path $workDir 'installer'
    New-Item -ItemType Directory -Path $installerStage -Force | Out-Null
    $installerPath = Join-Path $dist $installerName
    & $dotnet publish $installerProject -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        "-p:ZipPath=$zip" "-p:Version=$Version" -o $installerStage -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw "installer publish failed: $LASTEXITCODE" }
    $builtInstaller = Join-Path $installerStage 'v2crackN-Installer.exe'
    Move-Item $builtInstaller $installerPath
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
