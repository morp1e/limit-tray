# Security

Lim'it reads your Claude OAuth token from `~/.claude/.credentials.json` on every request.
That is the whole reason this file exists: a tool that touches a credential should say how
to report a problem with it, and what it already promises.

## Reporting a vulnerability

Use GitHub's private reporting: **Security → Report a vulnerability** on this repository.
That keeps the report out of public issues until there is a fix.

Please do not open a public issue for anything that could expose a token, a credentials
file, or a way to make the application send that token somewhere it should not go.

This is a personal project, not a funded one. There is no bounty and no response-time
commitment. What there is: I read the reports, and a credible one gets fixed before
anything else.

## What the application promises

These are testable claims, not intentions:

- The token is read fresh from disk per request and never stored in a field.
- It is sent only to `api.anthropic.com`, in the `Authorization` header.
- It is never written to disk, logged, displayed in the UI or tooltip, or included in an
  error message. Error text carries a status code or an exception type name and nothing
  else. There is a test asserting this.
- The only file written is `%LOCALAPPDATA%\limit-tray\history.json`, containing
  percentages, window lengths and timestamps. No token, no account identifier, no request
  or response body, no error text.
- No telemetry, no analytics, and no network request other than the usage endpoint above.

If you find any of these to be false, that is a vulnerability report, and it is the kind I
most want to receive.

## Things that are known and are not vulnerabilities

- **The released binary is unsigned,** so SmartScreen warns on first run. Every release
  publishes a `.sha256` beside the executable, and the binary is built by GitHub Actions
  from the tagged commit rather than uploaded from a machine.
- **Neither data source is a documented API.** The `anthropic-beta` header is undocumented
  and `codex app-server` is experimental. Either can change or disappear. When that
  happens the application reports "API changed" rather than guessing a number.

## Scope

This repository only. Claude Code, the Codex CLI and the endpoints they authenticate
against belong to Anthropic and OpenAI; report issues in those to them.
