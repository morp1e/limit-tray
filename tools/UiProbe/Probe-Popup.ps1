<#
.SYNOPSIS
  Drives the Lim'it popup through a click sequence and captures what the screen shows.

.DESCRIPTION
  A screenshot of a settled window proves nothing about what happens during a click.
  This script starts the app with --show, then for each step moves the real cursor,
  clicks, captures the popup 40 ms after the click and again once settled, and records
  the window rectangle. Two invariants are checked and printed:

    * the window rectangle never changes (the popup is a fixed-size layered window; a
      change means SizeToContent or repositioning crept back in), and
    * the process is still alive at the end.

  Coordinates are window-relative in physical pixels; the panel is bottom-aligned inside
  a 16 px margin. The script declares DPI awareness so a scaled display cannot fool it
  (see AGENTS.md: measure the artefact, then check the instrument).

  Output: <OutDir>\NN-<step>-t40.png and NN-<step>-settled.png, plus a one-line verdict.

.EXAMPLE
  pwsh -File tools/UiProbe/Probe-Popup.ps1 -Exe src/LimitTray.App/bin/Release/net9.0-windows/LimitTray.App.exe -OutDir docs/verify/latest
#>
param(
    [Parameter(Mandatory)] [string]$Exe,
    [string]$OutDir = "docs/verify/latest",
    [int]$StartupSeconds = 10
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class Probe {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L, T, Rt, B; }
}
'@
[Probe]::SetProcessDPIAware() | Out-Null
New-Item -ItemType Directory -Force $OutDir | Out-Null

$process = Start-Process $Exe -ArgumentList "--show" -PassThru
Start-Sleep $StartupSeconds

function Find-Popup {
    $hits = New-Object System.Collections.ArrayList
    [Probe]::EnumWindows({ param($h, $l)
        $ownerPid = 0; [Probe]::GetWindowThreadProcessId($h, [ref]$ownerPid) | Out-Null
        if ($ownerPid -eq $process.Id -and [Probe]::IsWindowVisible($h)) {
            $sb = New-Object System.Text.StringBuilder 256; [Probe]::GetWindowText($h, $sb, 256) | Out-Null
            if ($sb.ToString() -eq "Lim'it") { [void]$hits.Add($h) }
        }
        return $true }, [IntPtr]::Zero) | Out-Null
    if ($hits.Count) { $hits[0] } else { $null }
}

$handle = Find-Popup
if (-not $handle) { "FAIL: popup did not appear (alive=$(-not $process.HasExited))"; Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue; exit 1 }
$rect = New-Object Probe+R; [Probe]::GetWindowRect($handle, [ref]$rect) | Out-Null
$initial = "$($rect.L),$($rect.T),$($rect.Rt),$($rect.B)"
"window $initial"

function Capture($name) {
    $bmp = New-Object System.Drawing.Bitmap ($rect.Rt - $rect.L), ($rect.B - $rect.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.L, $rect.T, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $OutDir "$name.png")); $g.Dispose(); $bmp.Dispose()
}

# Panel geometry: 16 px margin, bottom-aligned; collapsed panel is 271 px tall.
$margin = 16; $collapsed = 271
$bottom = $rect.B - $margin
$steps = @(
    @{ name = "expand-claude";   x = 196; y = $bottom - $collapsed + 95 },
    @{ name = "collapse-claude"; x = 196; y = $bottom - 411 + 95 },
    @{ name = "open-settings";   x = 332; y = $bottom - $collapsed + 27 },
    @{ name = "back-to-panel";   x = 70;  y = $bottom - 530 + 505 }
)

$rectChanged = $false
$index = 0
Capture ("{0:00}-start" -f $index)
foreach ($step in $steps) {
    $index++
    [Probe]::SetCursorPos($rect.L + $step.x, $step.y) | Out-Null
    Start-Sleep -Milliseconds 250
    [Probe]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); [Probe]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 40
    Capture ("{0:00}-{1}-t40" -f $index, $step.name)
    Start-Sleep -Milliseconds 700
    Capture ("{0:00}-{1}-settled" -f $index, $step.name)
    $h = Find-Popup
    if ($h) {
        $now = New-Object Probe+R; [Probe]::GetWindowRect($h, [ref]$now) | Out-Null
        $current = "$($now.L),$($now.T),$($now.Rt),$($now.B)"
        if ($current -ne $initial) { $rectChanged = $true; "  rect changed at $($step.name): $current" }
    } else {
        "  popup hidden after $($step.name)"
    }
}

$alive = -not $process.HasExited
Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
if ($alive -and -not $rectChanged) { "PASS: $($steps.Count) clicks, window rect constant, process alive; frames in $OutDir" }
else { "FAIL: alive=$alive rectChanged=$rectChanged; frames in $OutDir"; exit 1 }
