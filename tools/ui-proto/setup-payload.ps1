# Prepares the runtime payload for the v2crackN client (publish\ folder):
#   publish\bin        - junction to the main app's cores (xray, geodata, dpi)
#   publish\guiConfigs - own copy of config + profile DB (isolated from the main app)
#   publish\binConfigs, guiLogs, guiTemps, guiBackups - ServiceLib working dirs
#
# Usage:  powershell -ExecutionPolicy Bypass -File setup-payload.ps1 [-Refresh]
#   -Refresh  re-copy guiConfigs from the main app (wipes client-side settings)

param(
    [switch]$Refresh
)

$ErrorActionPreference = 'Stop'

$protoDir = $PSScriptRoot
$outDir = Join-Path $protoDir 'publish'
$repoRoot = Split-Path -Parent (Split-Path -Parent $protoDir)
$releaseRoot = Join-Path $repoRoot 'v2rayN\v2rayN\bin\Release'

if (-not (Test-Path $releaseRoot)) {
    throw "main app build dir not found: $releaseRoot"
}

$rel = Get-ChildItem $releaseRoot -Directory |
    Where-Object { $_.Name -like 'net10.0-windows*' } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $rel) {
    $rel = Get-ChildItem $releaseRoot -Directory |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

if ($null -eq $rel) {
    throw "no build output found under $releaseRoot - build the main app first"
}

Write-Host "build dir: $($rel.FullName)"

New-Item -ItemType Directory -Path $outDir -Force | Out-Null

# cores as a junction (no 150 MB copy, no admin rights needed)
$binLink = Join-Path $outDir 'bin'
if (Test-Path $binLink) {
    Write-Host "bin: already present"
}
else {
    New-Item -ItemType Junction -Path $binLink -Target (Join-Path $rel.FullName 'bin') | Out-Null
    Write-Host "bin: junction -> $($rel.FullName)\bin"
}

# own copy of config + profiles
$srcCfg = Join-Path $rel.FullName 'guiConfigs'
$dstCfg = Join-Path $outDir 'guiConfigs'
if ($Refresh -or -not (Test-Path (Join-Path $dstCfg 'guiNConfig.json'))) {
    New-Item -ItemType Directory -Path $dstCfg -Force | Out-Null
    Copy-Item (Join-Path $srcCfg '*') $dstCfg -Recurse -Force
    # свежая копия обнуляет восстановление: эталон и порт создаются заново
    Remove-Item (Join-Path $dstCfg 'guiNConfig.backup.json') -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $outDir 'payload-state.json') -Force -ErrorAction SilentlyContinue
    Write-Host "guiConfigs: copied from main app"
}
else {
    Write-Host "guiConfigs: already present (use -Refresh to resync)"
}

foreach ($d in 'binConfigs', 'guiLogs', 'guiTemps', 'guiBackups') {
    New-Item -ItemType Directory -Path (Join-Path $outDir $d) -Force | Out-Null
}

$meta = @{ binSource = (Join-Path $rel.FullName 'bin') }
$meta | ConvertTo-Json | Set-Content (Join-Path $outDir 'payload.json') -Encoding ASCII

Write-Host "payload ready: $outDir"
