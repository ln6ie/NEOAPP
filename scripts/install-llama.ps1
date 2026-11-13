param(
    [string]$Source = (Join-Path (Split-Path -Parent $PSScriptRoot) 'llama-b12360-bin-win-cuda-12.4-x64'),
    [string]$CudaSource = (Join-Path $env:USERPROFILE '.lmstudio/extensions/backends/vendor/win-llama-cuda12-vendor-v2')
)
$ErrorActionPreference = 'Stop'
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$target = if ($admin) { Join-Path $env:ProgramFiles 'llama.cpp' } else { Join-Path $env:LOCALAPPDATA 'Programs/llama.cpp' }
if (-not (Test-Path -LiteralPath (Join-Path $Source 'llama-server.exe'))) { throw 'Existing llama-server.exe not found.' }
foreach ($name in @('cublas64_12.dll', 'cublasLt64_12.dll', 'cudart64_12.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $CudaSource $name))) { throw "Existing CUDA dependency missing: $name" }
}
New-Item -ItemType Directory -Path $target -Force | Out-Null
Copy-Item -Path (Join-Path $Source '*') -Destination $target -Recurse -Force
foreach ($name in @('cublas64_12.dll', 'cublasLt64_12.dll', 'cudart64_12.dll')) { Copy-Item -LiteralPath (Join-Path $CudaSource $name) -Destination $target -Force }
$ErrorActionPreference = 'Continue' # Native --version writes its success banner to stderr in b12360.
$version = & (Join-Path $target 'llama-server.exe') --version 2>&1
if ($LASTEXITCODE -ne 0 -or "$version" -notmatch '12360') { throw 'Installed executable failed b12360 verification.' }
$devices = & (Join-Path $target 'llama-server.exe') --list-devices 2>&1
if ($LASTEXITCODE -ne 0 -or "$devices" -notmatch 'CUDA0') { throw 'Installed executable cannot load CUDA0.' }
$ErrorActionPreference = 'Stop'
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$entries = @($userPath -split ';' | Where-Object { $_ })
if (-not ($entries | Where-Object { [Environment]::ExpandEnvironmentVariables($_).TrimEnd('\') -ieq $target.TrimEnd('\') })) {
    [Environment]::SetEnvironmentVariable('Path', (($entries + $target) -join ';'), 'User')
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class NEOEnvironmentBroadcast {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wparam, string lparam, uint flags, uint timeout, out IntPtr result);
}
'@
$broadcastResult = [IntPtr]::Zero
[void][NEOEnvironmentBroadcast]::SendMessageTimeout([IntPtr]0xffff, 0x1a, [IntPtr]::Zero, 'Environment', 2, 2000, [ref]$broadcastResult)
# Child shells inherit this fresh user/machine PATH, not the parent VS Code's stale PATH.
$env:Path = [Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [Environment]::GetEnvironmentVariable('Path','User')
& powershell -NoProfile -Command 'llama-server --version; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; llama-server --list-devices'
if ($LASTEXITCODE -ne 0) { throw 'Fresh PowerShell verification failed.' }
$manifest = Get-ChildItem -LiteralPath $target -File | Where-Object Name -ne 'NEO-install-manifest.json' | ForEach-Object { [pscustomobject]@{ Name=$_.Name; Length=$_.Length; SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } }
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $target 'NEO-install-manifest.json') -Encoding UTF8
Write-Host "Installed existing binaries to $target. Models were not moved or modified."
