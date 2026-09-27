<#
.SYNOPSIS
Measures Lim'it's memory and CPU footprint the way the v0.4 success criteria are defined.

.DESCRIPTION
Starts the given exe with --fixture and a fresh --data-dir (so no real API is called
and nothing touches the user's files), lets it warm up, then samples it every
-IntervalSeconds: Private Bytes (the number the criteria use; Task Manager's "Memory"
column is the working set and drops when pages are trimmed without freeing anything),
working set, handles, threads and CPU time. Scenarios:

  Idle   popup never opened.
  Cycle  idle, then open -> expand the first card -> collapse -> settings -> back -> close,
         then idle again. Reports how far memory settles above the idle floor.
  Open   popup open for the sampling period (animations finished).

The popup is driven through the host's "LimitTray.Probe" window message and clicks
posted straight to the popup window. The real cursor is never moved: v0.3's probe used
SetCursorPos and could not run while someone was using the mouse.

Proves: memory and CPU of the given build under a fixture. Does not prove: behaviour
with real providers (network stack, codex.exe reads), which must be run once by hand.

.EXAMPLE
pwsh tools/Footprint/Measure-Footprint.ps1 -Exe publish/LimitTray.exe -Scenario Cycle -Fixture tools/Footprint/fixtures/normal.json
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [ValidateSet('Idle', 'Cycle', 'Open')] [string] $Scenario = 'Idle',
    [string] $Fixture = (Join-Path $PSScriptRoot 'fixtures/normal.json'),
    [int] $WarmupSeconds = 30,
    [int] $Seconds = 120,
    [int] $IntervalSeconds = 5,
    [string] $Out,
    [double] $MaxIdleMB = 20,
    [double] $MaxCycleDeltaMB = 3
)

$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture

Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class ProbeWin32 {
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
"@

$dataDir = Join-Path ([IO.Path]::GetTempPath()) ("limit-footprint-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dataDir | Out-Null
$process = Start-Process -FilePath $Exe -ArgumentList @('--fixture', "`"$Fixture`"", '--data-dir', "`"$dataDir`"") -PassThru

function Find-Window([string] $class) {
    for ($i = 0; $i -lt 50; $i++) {
        $h = [ProbeWin32]::FindWindow($class, $null)
        if ($h -ne [IntPtr]::Zero) {
            [uint32] $owner = 0
            [ProbeWin32]::GetWindowThreadProcessId($h, [ref] $owner) | Out-Null
            if ($owner -eq $process.Id) { return $h }
        }
        Start-Sleep -Milliseconds 100
    }
    return [IntPtr]::Zero
}

$probe = [ProbeWin32]::RegisterWindowMessage('LimitTray.Probe')
function Send-Probe([int] $command) {
    $hostWindow = Find-Window 'LimitTray.Host'
    if ($hostWindow -eq [IntPtr]::Zero) { throw 'host window not found' }
    [ProbeWin32]::PostMessage($hostWindow, $probe, [IntPtr] $command, [IntPtr]::Zero) | Out-Null
}

# Clicks at a point given in panel DIPs (the popup draws the panel 16 DIPs inside its window).
function Send-Click([double] $x, [double] $y) {
    $popup = Find-Window 'LimitTray.Popup'
    if ($popup -eq [IntPtr]::Zero) { throw 'popup window not found' }
    $scale = [ProbeWin32]::GetDpiForWindow($popup) / 96.0
    $px = [int][math]::Round(($x + 16) * $scale); $py = [int][math]::Round(($y + 16) * $scale)
    $lParam = [IntPtr](($py -shl 16) -bor ($px -band 0xFFFF))
    [ProbeWin32]::PostMessage($popup, 0x0200, [IntPtr]::Zero, $lParam) | Out-Null   # WM_MOUSEMOVE
    [ProbeWin32]::PostMessage($popup, 0x0202, [IntPtr]::Zero, $lParam) | Out-Null   # WM_LBUTTONUP
}

$rows = [System.Collections.Generic.List[object]]::new()
$lastCpu = 0.0
function Sample([string] $phase, [int] $seconds) {
    $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) {
        Start-Sleep -Seconds $IntervalSeconds
        $process.Refresh()
        if ($process.HasExited) { throw "the app exited with code $($process.ExitCode)" }
        $cpu = $process.TotalProcessorTime.TotalMilliseconds
        $rows.Add([pscustomobject]@{
            phase = $phase
            time = (Get-Date).ToString('HH:mm:ss', $inv)
            private_mb = [math]::Round($process.PrivateMemorySize64 / 1MB, 2)
            working_set_mb = [math]::Round($process.WorkingSet64 / 1MB, 2)
            handles = $process.HandleCount
            threads = $process.Threads.Count
            cpu_ms = [math]::Round($cpu - $script:lastCpu)
        })
        $script:lastCpu = $cpu
    }
}

function Median([double[]] $values) {
    if ($values.Count -eq 0) { return [double]::NaN }
    $sorted = $values | Sort-Object
    return $sorted[[int][math]::Floor($sorted.Count / 2)]
}

try {
    Start-Sleep -Seconds $WarmupSeconds
    $process.Refresh(); $lastCpu = $process.TotalProcessorTime.TotalMilliseconds

    switch ($Scenario) {
        'Idle' { Sample 'idle' $Seconds }
        'Open' {
            Send-Probe 1; Start-Sleep -Seconds 2
            Sample 'open' $Seconds
            Send-Probe 1
        }
        'Cycle' {
            Sample 'idle' $Seconds
            Send-Probe 1; Start-Sleep -Milliseconds 800
            Send-Click 180 90; Start-Sleep -Milliseconds 800          # first card: expand
            Send-Click 180 90; Start-Sleep -Milliseconds 800          # collapse
            Send-Probe 2; Start-Sleep -Milliseconds 800               # settings
            Send-Click 40 28; Start-Sleep -Milliseconds 800           # back arrow
            Sample 'open' 10
            Send-Probe 1; Start-Sleep -Seconds 2                      # close
            Sample 'after' $Seconds
        }
    }
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Remove-Item -Recurse -Force $dataDir -ErrorAction SilentlyContinue
}

if ($Out) { $rows | Export-Csv -Path $Out -NoTypeInformation -Encoding utf8 }

$summary = foreach ($group in ($rows | Group-Object phase)) {
    $private = [double[]]($group.Group | ForEach-Object private_mb)
    $cpu = [double[]]($group.Group | ForEach-Object cpu_ms)
    [pscustomobject]@{
        phase = $group.Name
        samples = $group.Count
        private_median_mb = Median $private
        private_max_mb = ($private | Measure-Object -Maximum).Maximum
        cpu_ms_per_min = [math]::Round(($cpu | Measure-Object -Sum).Sum / ($group.Count * $IntervalSeconds) * 60)
        idle_windows_at_0ms = @($cpu | Where-Object { $_ -eq 0 }).Count
        max_handles = ($group.Group | Measure-Object handles -Maximum).Maximum
        max_threads = ($group.Group | Measure-Object threads -Maximum).Maximum
    }
}
$summary | Format-Table -AutoSize | Out-String -Width 200 | Write-Output

$failed = $false
$idle = $summary | Where-Object phase -eq 'idle'
if ($idle -and $idle.private_median_mb -gt $MaxIdleMB) {
    Write-Output ("FAIL idle private median {0} MB > {1} MB" -f $idle.private_median_mb.ToString($inv), $MaxIdleMB)
    $failed = $true
}
$after = $summary | Where-Object phase -eq 'after'
if ($idle -and $after -and ($after.private_median_mb - $idle.private_median_mb) -gt $MaxCycleDeltaMB) {
    Write-Output ("FAIL after-cycle median is {0} MB above idle (limit {1})" -f ($after.private_median_mb - $idle.private_median_mb).ToString($inv), $MaxCycleDeltaMB)
    $failed = $true
}
if ($failed) { exit 1 }
Write-Output 'PASS'
