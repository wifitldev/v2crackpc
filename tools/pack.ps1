<#
.SYNOPSIS
    Builds a ready-to-ship v2crackN release for Windows.

.DESCRIPTION
    1. dotnet publish  -> single self-contained v2crackN.exe (no .NET needed on the target PC)
    2. copies the cores (xray / sing-box) and geo files into bin\
    3. zips everything into dist\v2crackN-windows-64.zip

    The asset name must match Global.AppReleaseAssetName in ServiceLib\Global.cs
    ("v2crackN") so that the in-app updater can pick it up from GitHub releases.

.EXAMPLE
    .\tools\pack.ps1
    .\tools\pack.ps1 -SkipPublish          # reuse the previous publish output
#>
[CmdletBinding()]
param(
    # folder holding xray.exe, geoip.dat, geosite.dat
    [string]$CoreSource = "$env:APPDATA\v2crackNG-Win\core",
    # standalone sing-box.exe (optional, needed for hysteria2/tuic nodes)
    [string]$SingBoxExe = "C:\Program Files\FlyFrogLLC\Happ\tun\sing-box.exe",
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$root   = Split-Path -Parent $PSScriptRoot          # repo root
$proj   = Join-Path $root 'v2rayN\v2rayN\v2rayN.csproj'
$pubDir = Join-Path $root 'publish\win-x64'
$dist   = Join-Path $root 'dist'
$asset  = 'v2crackN-windows-64.zip'

$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

# ---------------------------------------------------------------- 1. publish
if (-not $SkipPublish) {
    Write-Host '== publish ==' -ForegroundColor Cyan
    if (Test-Path $pubDir) { Remove-Item $pubDir -Recurse -Force }
    & $dotnet publish $proj -c Release -r win-x64 --self-contained true -o $pubDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}

$exe = Join-Path $pubDir 'v2crackN.exe'
if (-not (Test-Path $exe)) { throw "publish output not found: $exe" }

# ------------------------------------------------------------------- 2. cores
Write-Host '== cores ==' -ForegroundColor Cyan
$bin = Join-Path $pubDir 'bin'
New-Item -ItemType Directory -Force -Path (Join-Path $bin 'xray'), (Join-Path $bin 'sing_box') | Out-Null

function Copy-Required([string]$from, [string]$to) {
    if (-not (Test-Path $from)) { Write-Warning "missing: $from"; return $false }
    Copy-Item $from $to -Force
    Write-Host ("  {0,-28} -> {1}" -f (Split-Path $from -Leaf), $to.Replace($pubDir, '.'))
    return $true
}

$ok = $true
$ok = (Copy-Required (Join-Path $CoreSource 'xray.exe')    (Join-Path $bin 'xray\xray.exe')) -and $ok
$ok = (Copy-Required (Join-Path $CoreSource 'geoip.dat')   (Join-Path $bin 'geoip.dat'))      -and $ok
$ok = (Copy-Required (Join-Path $CoreSource 'geosite.dat') (Join-Path $bin 'geosite.dat'))    -and $ok
if (Test-Path (Join-Path $CoreSource 'wintun.dll')) {
    Copy-Required (Join-Path $CoreSource 'wintun.dll') (Join-Path $bin 'wintun.dll') | Out-Null
}
if (Test-Path $SingBoxExe) {
    Copy-Required $SingBoxExe (Join-Path $bin 'sing_box\sing-box.exe') | Out-Null
}
else {
    Write-Warning "sing-box not found - hysteria2/tuic nodes will not work"
}
if (-not $ok) { throw 'xray core is missing - aborting' }

# --------------------------------------------------------------------- 3. zip
Write-Host '== zip ==' -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist $asset
if (Test-Path $zip) { Remove-Item $zip -Force }

Compress-Archive -Path (Join-Path $pubDir '*') -DestinationPath $zip -CompressionLevel Optimal
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)

# read the current version so the hint never goes stale
$props = Join-Path $root 'v2rayN\Directory.Build.props'
$ver = '?'
if (Test-Path $props) {
    $m = [regex]::Match((Get-Content $props -Raw), '<Version>([^<]+)</Version>')
    if ($m.Success) { $ver = $m.Groups[1].Value }
}

Write-Host ''
Write-Host "OK  $zip  ($size MB)" -ForegroundColor Green
Write-Host "Upload it to a GitHub release with the tag $ver -"
Write-Host 'the in-app updater looks for exactly this asset name.'
