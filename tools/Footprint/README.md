# Footprint tools

Lim'it's main requirement is a small, quiet process. These tools measure that instead
of assuming it.

## Measure-Footprint.ps1

Starts a build with a fixture (no network, no `codex.exe`, a throwaway data directory),
samples it and checks the v0.4 success criteria:

| Criterion | Default limit |
|---|---|
| Private Bytes, popup never opened | 20 MB (`-MaxIdleMB`) |
| Private Bytes after open/expand/settings/close, above the idle floor | 3 MB (`-MaxCycleDeltaMB`) |

```powershell
pwsh tools/Footprint/Measure-Footprint.ps1 -Exe <path>\LimitTray.exe -Scenario Idle
pwsh tools/Footprint/Measure-Footprint.ps1 -Exe <path>\LimitTray.exe -Scenario Cycle -Out cycle.csv
```

Private Bytes is the number that counts. Task Manager's "Memory" column is the private
working set, which drops when pages are trimmed and says little about what the process
has committed.

The popup is driven through the host window's `LimitTray.Probe` message and clicks
posted to the popup window, so the mouse is never moved.

## Visual checks without a screen

The app itself can render the popup or the tray icons from a fixture into a PNG:

```powershell
LimitTray.exe --render panel.png --fixture tools/Footprint/fixtures/screenshot.json --expand claude
LimitTray.exe --render settings.png --fixture tools/Footprint/fixtures/normal.json --page settings
LimitTray.exe --render list.png --fixture tools/Footprint/fixtures/normal.json --page settings --open-list refresh
LimitTray.exe --render icons.png --fixture tools/Footprint/fixtures/warning.json --icons
```

Other switches: `--theme light`, `--scale 1.5`, `--lang en`, `--hover claude`,
`--save-failed`. `screenshot.json` reproduces the data of `docs/screenshot.png`, so the two
can be compared pixel for pixel.

## Fixtures

`fixtures/*.json` use relative times (`ageSeconds`, `resetsInMinutes`, samples as
`[minutesAgo, percent]`), so they never expire. A provider may appear more than once; the
entries are applied in order, which is how `ratelimited.json` shows retained numbers under
an HTTP 429.
