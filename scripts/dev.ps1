$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src/App/NEO.csproj'
$existing = @(Get-CimInstance Win32_Process -Filter "Name = 'NEO.exe'")
if ($existing.Count -gt 0) {
    throw 'NEO is already running. Quit NEO from its tray menu, then run this command again.'
}
Push-Location $projectRoot
$previousLogPath = $env:NEO_LOG_PATH
$appProcess = $null
$reader = $null
try {
    dotnet build $project -c Debug
    if ($LASTEXITCODE -ne 0) { throw 'NEO Debug build failed.' }
    $logDirectory = Join-Path $projectRoot 'artifacts/logs'
    [IO.Directory]::CreateDirectory($logDirectory) | Out-Null
    $env:NEO_LOG_PATH = Join-Path $logDirectory ("NEO-dev-{0}-{1}.jsonl" -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $PID)
    [IO.File]::WriteAllText($env:NEO_LOG_PATH, '')
    Write-Host "NEO development log: $env:NEO_LOG_PATH" -ForegroundColor Cyan
    Write-Host 'Logs appear below. Ctrl+C stops this development instance. Quit NEO from the tray to exit normally.' -ForegroundColor Cyan
    $stream = [IO.File]::Open($env:NEO_LOG_PATH, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    $reader = [IO.StreamReader]::new($stream)
    $appProcess = Start-Process -FilePath (Join-Path $projectRoot 'src/App/bin/Debug/net10.0-windows/NEO.exe') -PassThru
    do {
        while (($line = $reader.ReadLine()) -ne $null) { Write-Host $line }
        if ($appProcess.HasExited) { break }
        Start-Sleep -Milliseconds 100
    } while ($true)
    while (($line = $reader.ReadLine()) -ne $null) { Write-Host $line }
    Write-Host "NEO exit code: $($appProcess.ExitCode)"
}
finally {
    if ($reader) { $reader.Dispose() }
    if ($appProcess -and -not $appProcess.HasExited) { Stop-Process -Id $appProcess.Id }
    $env:NEO_LOG_PATH = $previousLogPath
    Pop-Location
}
