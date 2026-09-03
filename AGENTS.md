# Lim'it — instructions for AI coding agents

Windows tray app showing Claude Code and Codex CLI quota in one panel.

This file is context for AI coding agents working on this repository. If you are a human,
[README.md](README.md) is the one you want.

## Rules that hold in this repo

- **All logic lives in `src/LimitTray.Core`, which must never reference WPF or WinForms.**
  `src/LimitTray.App` only draws. This split is what keeps the logic testable headlessly — do
  not move business rules into the UI project.
- **No failure state is ever rendered as `0%`.** `0%` is a real value a provider can genuinely
  report. Failures travel as `HealthState` (`RateLimited`, `AuthMissing`, `ProtocolBroken`,
  `Stale`) and are shown in words. Confusing an error with zero is the one defect that would
  make this app worse than useless.
- **The token is never logged, written to disk, displayed, or included in an exception
  message.** Error text carries a status code or an exception type name, nothing more. There
  is a test enforcing this; do not weaken it.
- **No external NuGet dependencies** beyond the test project's xUnit.
- **User-facing text is bilingual (English and Turkish) and lives in
  `src/LimitTray.Core/Presentation/Strings.cs`.** Never hardcode a display string anywhere
  else, including XAML code-behind. Language selection is injectable so tests can pin it.
- **Source files, identifiers, file names and commit messages are ASCII.** The Turkish display
  strings in `Strings.cs` are the deliberate exception and use proper accented characters.
- **Neither data source is an official API.** The `anthropic-beta` header is undocumented and
  `codex app-server` is experimental. When a source changes shape, the correct behaviour is to
  report `ProtocolBroken` — never to guess, interpolate, or substitute a plausible number.
- **Never bend production behaviour to make a test pass.** If a test cannot pass against
  correct code, the test is wrong: stop and say so rather than changing the code under it.
  This happened during development and cost a review cycle.
- **A green test suite is not proof the app works.** The unit tests here talk to fakes, and a
  fake process has no encoding, no real stdio and no clock. Run the app against the real
  providers before claiming a change works. The two worst defects in this project's history —
  a BOM that silenced the entire Codex side, and correct data mislabelled as stale — both
  passed every test.

## Documents

- Design: [`docs/specs/2026-09-03-design.md`](docs/specs/2026-09-03-design.md)
- Implementation plan:
  [`docs/plans/2026-09-03-implementation-plan.md`](docs/plans/2026-09-03-implementation-plan.md)

Both are historical records written before and during implementation. They still refer to the
project by its original name, Agent Quota Tray; that is deliberate and they are not rewritten.
