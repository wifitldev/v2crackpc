# Rebuild and launch the new UI without forcibly terminating a running instance.
$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$project = Join-Path $projectDir 'UiProto.csproj'
$outputDir = Join-Path $projectDir 'bin\Release\net10.0-windows10.0.19041.0'
$exe = Join-Path $outputDir 'v2crackN.exe'

try {
    $running = @(Get-Process -Name 'v2crackN' -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -eq $exe } catch { $false }
    })
    foreach ($process in $running) {
        Write-Host "Closing running instance (PID $($process.Id))..."
        if (-not $process.CloseMainWindow()) {
            throw 'The running instance has no window to close. Exit it from the tray and retry.'
        }
        if (-not $process.WaitForExit(15000)) {
            throw 'The application did not exit. Exit it from the tray and retry; it was not forcibly terminated.'
        }
    }

    Write-Host 'Building Release...'
    & dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Executable not found: $exe" }

    $core = Join-Path $outputDir 'bin\xray\xray.exe'
    if (-not (Test-Path -LiteralPath $core -PathType Leaf)) {
        Write-Warning 'Xray is missing from the output bin\xray folder; connections requiring it will not work.'
    }
    Write-Host 'Starting v2crackN...'
    Start-Process -FilePath $exe -WorkingDirectory $outputDir
    Write-Host 'Done.'
}
catch {
    Write-Error $_
    exit 1
}
