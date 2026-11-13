$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $projectRoot 'NEO.sln'
$project = Join-Path $projectRoot 'src/App/NEO.csproj'
$output = Join-Path $projectRoot 'artifacts/NEO-win-x64'

Push-Location $projectRoot
try {
    Write-Host 'Restoring NEO...' -ForegroundColor Cyan
    dotnet restore $solution
    if ($LASTEXITCODE -ne 0) { throw 'NEO restore failed.' }

    Write-Host 'Building NEO...' -ForegroundColor Cyan
    dotnet build $solution -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'NEO build failed.' }

    Write-Host 'Running NEO tests...' -ForegroundColor Cyan
    dotnet test $solution -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'NEO tests failed.' }

    Write-Host "Publishing Windows x64 package to $output ..." -ForegroundColor Cyan
    dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o $output
    if ($LASTEXITCODE -ne 0) { throw 'NEO publish failed.' }

    $exe = Join-Path $output 'NEO.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw "Windows publish completed without producing NEO.exe."
    }

    $size = [math]::Round((Get-ChildItem -LiteralPath $output -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB, 2)
    Write-Host "Windows build ready: $exe ($size MiB)" -ForegroundColor Green
}
finally {
    Pop-Location
}
