# Lim'it: instructions for AI coding agents

Windows tray app showing Claude Code and Codex CLI quota in one panel.

This file is context for AI coding agents working on this repository. If you are a human,
[README.md](README.md) is the one you want.

## Rules that hold in this repo

- **All logic lives in `src/LimitTray.Core`, which must never reference WPF or WinForms.**
  `src/LimitTray.App` only draws. This split is what keeps the logic testable headlessly.
  Do not move business rules into the UI project.
- **No failure state is ever rendered as `0%`.** `0%` is a real value a provider can genuinely
  report. Failures travel as `HealthState` (`RateLimited`, `AuthMissing`, `ProtocolBroken`,
  `Stale`) and are shown in words. Confusing an error with zero is the one defect that would
  make this app worse than useless.
- **The token is never logged, written to disk, displayed, or included in an exception
  message.** Error text carries a status code or an exception type name, nothing more. There
  is a test enforcing this; do not weaken it.
- **No external NuGet dependencies** beyond the test project's xUnit. This includes the
  icon generator in `tools/IconGen`, which reaches System.Drawing through the Windows
  Desktop reference pack rather than a package.
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
- **Verify the screen with a DPI-aware capture.** This display runs at 150%. A capture
  tool that has not called `SetProcessDPIAware` reads a 480x930 window as 320x620 and
  silently crops the right third, which looks exactly like a rendering bug and cost a long
  detour hunting a percentage that was never missing. Measure the artefact, then check the
  instrument.
- **A green test suite is not proof the app works.** The unit tests here talk to fakes, and a
  fake process has no encoding, no real stdio and no clock. Run the app against the real
  providers before claiming a change works. The two worst defects in this project's history,
  a BOM that silenced the entire Codex side and correct data mislabelled as stale, both
  passed every test.

## Documents

- Design: [`docs/specs/2026-09-03-design.md`](docs/specs/2026-09-03-design.md)
- Implementation plan:
  [`docs/plans/2026-09-03-implementation-plan.md`](docs/plans/2026-09-03-implementation-plan.md)

Both are historical records written before and during implementation. They still refer to the
project by its original name, Agent Quota Tray; that is deliberate and they are not rewritten.
