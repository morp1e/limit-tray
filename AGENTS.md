# Lim'it: instructions for AI coding agents

Windows tray app showing Claude Code and Codex CLI quota in one panel.

This file is context for AI coding agents working on this repository. If you are a human,
[README.md](README.md) is the one you want.

## Rules that hold in this repo

- **All logic lives in `src/LimitTray.Core`, which must never reference Win32 UI, Direct2D
  or DirectWrite.** `src/LimitTray.Native` hosts and draws. This split is what keeps the
  logic testable headlessly. Do not move business rules into the native project.
- **Memory is a feature, measured in Private Bytes.** The app shipped at 180 MB private under
  WPF; v0.4 exists to fix that. `tools/Footprint/Measure-Footprint.ps1` checks the limits
  (20 MB idle, back within 3 MB of idle after a popup cycle). Nothing that draws may outlive
  the popup: the window, surfaces, Direct2D and DirectWrite factories are created on open
  and disposed on close. The tray icon is repainted only when its Core model changes. While
  the popup is closed nothing wakes faster than the collectors' own schedule (Claude poll,
  Codex rollout check every 30 s, Codex read every 10 min) and a 60 s staleness check.
  `codex app-server` runs only during a read. Task Manager's "Memory" column is the working
  set and is not the number.
- **No failure state is ever rendered as `0%`.** `0%` is a real value a provider can genuinely
  report. Failures travel as `HealthState` (`RateLimited`, `AuthMissing`, `ProtocolBroken`,
  `Stale`) and are shown in words. Confusing an error with zero is the one defect that would
  make this app worse than useless.
- **The token is never logged, written to disk, displayed, or included in an exception
  message.** Error text carries a status code or an exception type name, nothing more. There
  is a test enforcing this; do not weaken it.
- **No runtime NuGet dependencies** beyond the test projects' xUnit and test SDK. This includes
  the icon generator in `tools/IconGen`, which reaches System.Drawing through the Windows
  Desktop reference pack rather than a package. The one exception is build-time only:
  `Microsoft.Windows.CsWin32` (`PrivateAssets="all"`) generates the Win32, Direct2D and
  DirectWrite signatures and adds nothing to the executable. Hand-written COM vtables fail
  silently on a wrong slot index.
- **A projection is only ever shown when the measurements support it.** The burn rate
  needs at least three samples spanning at least ten minutes on a window that is actually
  moving; below that the line is absent. Do not lower these thresholds to make the feature
  appear sooner. The same rule as `0%`: a number the data cannot justify is worse than no
  number.
- **Only percentages, window lengths and timestamps are persisted.** `history.json` must
  never gain the snapshot Detail, an account identifier, or anything derived from the
  token. Detail can carry an exception message, and this file sits on disk. There is a
  test asserting it; do not weaken it.
- **A drop in a percentage means the window reset, not that usage fell.** Fitting a rate
  across a reset turns two correct measurements into a confident wrong answer, so the
  series is dropped instead.
- **User-facing text is bilingual (English and Turkish) and lives in
  `src/LimitTray.Core/Presentation/Strings.cs`.** Never hardcode a display string anywhere
  else, including XAML code-behind. Language selection is injectable so tests can pin it.
- **Source files, identifiers, file names and commit messages are ASCII.** The Turkish display
  strings in `Strings.cs` are the deliberate exception and use proper accented characters.
- **Neither data source is an official API.** The `anthropic-beta` header is undocumented and
  `codex app-server` is experimental. When a source changes shape, the correct behaviour is to
  report `ProtocolBroken`, never to guess, interpolate, or substitute a plausible number.
- **Never bend production behaviour to make a test pass.** If a test cannot pass against
  correct code, the test is wrong: stop and say so rather than changing the code under it.
  This happened during development and cost a review cycle.
- **Verify the screen with a DPI-aware capture.** This display ran at 150% when this was
  written and at 100% on 2026-09-20; measure, do not assume. A capture
  tool that has not called `SetProcessDPIAware` reads a 480x930 window as 320x620 and
  silently crops the right third, which looks exactly like a rendering bug and cost a long
  detour hunting a percentage that was never missing. Measure the artefact, then check the
  instrument.
- **Colour is decided in `Theme.ColourFor` and nowhere else.** No hex value for a quota
  colour in the native project; the popup and the tray icon both ask Core. A lighter tint
  or an alpha may be derived in `Ui/Palette.cs`, but from the colour Core chose. The theme
  tokens (surface, card, text, track) live in `Ui/Palette.cs` and are v0.3's XAML values.
- **The tray icon draws `TrayIconModel` and decides nothing itself.** A null bar in the model
  is drawn as the question mark, never as an empty gauge, because empty reads as zero.
- **Visual work follows the reference, not the brief.** In v0.3 the pages built from a text
  brief compiled and passed, and did not look like the design that had been chosen. The
  agent that saw the reference does the visual layer and checks it. `LimitTray.exe --render`
  draws the popup or the tray icons from a fixture into a PNG (see `tools/Footprint/README.md`);
  `tools/Footprint/fixtures/screenshot.json` reproduces `docs/screenshot.png`, so the two can be
  diffed pixel for pixel. v0.4 was brought to the same heights and edges that way; three of the
  mismatches were WPF layout rules nobody had written down (a template that ignored Padding, a
  StackPanel that stretched its items, XAML line breaks between inlines becoming spaces).
  A render is pixels, not proof the app works: run the app too.
- **Accepted exception to the "never substitute a number" rule:** `CodexRateLimitsParser`
  fills a missing `windowDurationMins` with 5 hours / 7 days. The live app-server omits the
  field in some notifications, the value only labels the window and feeds retention, and
  the percentage itself is never guessed. Reviewed 2026-09-20 and kept on purpose.
- **The popup changes size only through `UpdateLayeredWindow`.** Content, size and position
  go to the screen in that one call, so a taller frame never shows the previous one. In
  v0.3.3 resizing the WPF window on expand made DWM show a ghost of the old panel; v0.3.4
  worked around it with a fixed 392x700 window. Do not resize the popup with `SetWindowPos`,
  and never apply DWM attributes (corners, border colour, backdrop) to it: on a layered
  window they made DWM paint a tinted rectangle on one display.
- **Every UI change is checked with a click sequence, not a single screenshot:** open,
  expand a card, collapse it, open settings, back, close. `Measure-Footprint.ps1 -Scenario
  Cycle` drives exactly that through the host's `LimitTray.Probe` message and clicks posted to
  the popup window, without moving the real cursor.
- **A green test suite is not proof the app works.** The unit tests here talk to fakes, and a
  fake process has no encoding, no real stdio and no clock. Run the app against the real
  providers before claiming a change works. The two worst defects in this project's history,
  a BOM that silenced the entire Codex side and correct data mislabelled as stale, both
  passed every test.

## Documents

- Current (v0.4, NativeAOT): [`docs/specs/2026-09-27-native-aot-design.md`](docs/specs/2026-09-27-native-aot-design.md),
  [`docs/plans/2026-09-27-native-aot-plan.md`](docs/plans/2026-09-27-native-aot-plan.md)
- v0.3 panel redesign: [`docs/specs/2026-09-20-popup-redesign-design.md`](docs/specs/2026-09-20-popup-redesign-design.md)
- First design: [`docs/specs/2026-09-03-design.md`](docs/specs/2026-09-03-design.md),
  [`docs/plans/2026-09-03-implementation-plan.md`](docs/plans/2026-09-03-implementation-plan.md)

These are historical records written before and during implementation. The oldest still
refer to the project by its original name, Agent Quota Tray; that is deliberate and they
are not rewritten.
