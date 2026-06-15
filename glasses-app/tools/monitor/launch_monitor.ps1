# launch_monitor.ps1 — open the Eagle Eye AR live monitor in this window.
# Usage:
#   powershell -NoExit -File launch_monitor.ps1
#   powershell -NoExit -File launch_monitor.ps1 -Serial A06B4A95B784973
# The skill normally calls this inside a freshly-spawned terminal window.

# Serial 비우면 adb 의 첫 디바이스 사용. Adb 기본값은 PATH 의 'adb'.
#   다른 머신: powershell -NoExit -File launch_monitor.ps1 -Serial <SER> [-Adb <path>]
param(
    [string]$Serial = "",
    [string]$Adb    = "adb"
)

$ErrorActionPreference = "Stop"
$script = Join-Path $PSScriptRoot "eagle_monitor.py"

# Make the console UTF-8 so box-drawing / emoji glyphs render.
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}
$env:PYTHONIOENCODING = "utf-8"

$pyArgs = @($script, "--adb", $Adb)
if ($Serial -ne "") { $pyArgs += @("--serial", $Serial) }

Write-Host "Eagle Eye monitor -> $(if($Serial){$Serial}else{'(first device)'}) (Ctrl-C to stop)" -ForegroundColor Cyan
python @pyArgs
