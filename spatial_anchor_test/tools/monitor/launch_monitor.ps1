# launch_monitor.ps1 — open the Eagle Eye AR live monitor in this window.
# Usage:
#   powershell -NoExit -File launch_monitor.ps1
#   powershell -NoExit -File launch_monitor.ps1 -Serial A06B4A95B784973
# The skill normally calls this inside a freshly-spawned terminal window.

param(
    [string]$Serial = "A06B4A95B784973",
    [string]$Adb    = "C:/Users/yulee/AppData/Local/Android/Sdk/platform-tools/adb.exe"
)

$ErrorActionPreference = "Stop"
$script = Join-Path $PSScriptRoot "eagle_monitor.py"

# Make the console UTF-8 so box-drawing / emoji glyphs render.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}
$env:PYTHONIOENCODING = "utf-8"

Write-Host "Eagle Eye monitor -> $Serial (Ctrl-C to stop)" -ForegroundColor Cyan
python $script --serial $Serial --adb $Adb
