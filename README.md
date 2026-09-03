# Lim'it

Claude Code and Codex CLI usage limits in your Windows tray. No session required.

![Lim'it popup showing Claude and Codex usage](docs/screenshot.png)

Both agents track a 5-hour and a 7-day window, but each hides that number inside its own
interactive session — `/usage` in Claude Code, `/status` in Codex. Lim'it puts both in one
place, so "how much do I have left?" does not cost you a session.

## Why this exists

I use both agents daily, and how much I can get done in a day now depends on those two
numbers. Checking them meant opening two sessions. So I built this for myself and put it
here in case it is useful to someone else.

There are already good tools in this space — several of them more mature, cross-platform, or
supporting more providers. If Lim'it does not fit you, try
[token-monitor](https://github.com/Javis603/token-monitor),
[Usage4Claude](https://github.com/f-is-h/Usage4Claude),
[brink](https://github.com/semihtalii/brink), or
[claude-codex-usage-dashboard](https://github.com/frankchiu-dev/claude-codex-usage-dashboard).

The one thing Lim'it is deliberate about: **an error never looks like `0%`.** If the token is
missing, the endpoint rate-limits, or an upstream API changes shape, the panel says so in
words. `0%` only ever means a real, measured zero. Getting that wrong is the one failure that
would make a quota display worse than useless.

## Requirements

Windows, and the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0).
You also need to be logged into whichever agent you want to track.

## Run

```
dotnet run --project src/LimitTray.App
```

Click the tray icon to open the panel. Right-click for exit.

## Test

```
dotnet test
```

## How it works

The two providers expose their quota in completely different ways, so Lim'it reads them
differently.

**Claude** — polls `GET https://api.anthropic.com/api/oauth/usage` every 60 seconds, sending
the OAuth token from `~/.claude/.credentials.json` and the header
`anthropic-beta: oauth-2025-04-20`. This is not a model call and does not consume your quota.
The endpoint has its own rate limit, so Lim'it backs off exponentially (2, 4, 8 … capped at
15 minutes) on HTTP 429.

**Codex** — speaks JSON-RPC to `codex app-server` over stdio, calling
`account/rateLimits/read` and listening for `account/rateLimits/updated` notifications. It
also re-reads on a timer, because app-server only pushes when the quota actually changes and
the data would otherwise look stale while being perfectly current. If app-server cannot be
started, Lim'it falls back to the last `rate_limits` block written into
`~/.codex/sessions/**/rollout-*.jsonl`, and clearly marks that data as stale.

All logic lives in `LimitTray.Core`, which has no WPF dependency and is covered by tests.
`LimitTray.App` only draws.

## Security and privacy

This app reads your Claude credentials file. You should want to know exactly what it does
with them, so:

- It reads `~/.claude/.credentials.json` for the OAuth access token, freshly on each request
  (Claude Code may have rotated it).
- The token is sent **only** to `api.anthropic.com`, in the `Authorization` header.
- The token is never written to disk, never logged, never shown in the UI or tooltip, and
  never included in an error message. Error text carries only a status code or an exception
  type name. There is a test asserting the token cannot leak into error details.
- Lim'it has no telemetry and no analytics, and makes no network request other than the usage
  endpoint above.

The code is short and the relevant file is
[`ClaudeCredentialReader.cs`](src/LimitTray.Core/Claude/ClaudeCredentialReader.cs) — please
read it rather than take my word for it.

## Stability warning

Neither data source is a documented, supported API.

- The `anthropic-beta: oauth-2025-04-20` header is undocumented and versioned by date.
  Anthropic can change or remove it without notice.
- `codex app-server` is marked experimental by OpenAI and its method names may change.

If either breaks, Lim'it shows "API changed" rather than a wrong number — but it stops being
useful until the code is updated. Do not build anything important on top of it.

## How this was built

The C# was written by OpenAI's Codex, task by task, against a spec and an implementation plan
I wrote and reviewed. Every task's build, tests and behaviour were verified before it was
committed. The spec and plan are in [`docs/`](docs/) if you want to see the actual process,
including the defects that came out of it.

One of those is worth repeating, because it is why the plan insists on running the real app
instead of trusting a green test suite: with 67 tests passing, the Codex side displayed
nothing at all. `Encoding.UTF8` in .NET emits a BOM, so the first JSON-RPC write put
`EF BB BF` in front of the message; app-server failed to deserialize it, wrote the error to a
stderr nobody was reading, and answered nothing. No unit test could catch that — a fake
process has no encoding. There is now a regression test asserting the encoding emits no
preamble.

`AGENTS.md` in this repo is instruction context for AI coding agents working on it.

## License

MIT — see [LICENSE](LICENSE).
