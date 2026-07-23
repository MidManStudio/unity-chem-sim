param([switch]$Release = $true)

$Root = Split-Path -Parent $PSScriptRoot
$Out  = Join-Path $Root "Assets\Plugins"
New-Item -ItemType Directory -Force -Path $Out | Out-Null

Write-Host "→ Building chemistry_core..." -ForegroundColor Yellow
cargo build -p chemistry_core --release

$Dll = Join-Path $Root "target\release\chemistry_core.dll"
if (Test-Path $Dll) {
    Copy-Item $Dll $Out -Force
    Write-Host "✓ chemistry_core.dll copied to Assets/Plugins/" -ForegroundColor Green
} else {
    Write-Error "Build succeeded but DLL not found at: $Dll"
    exit 1
}
