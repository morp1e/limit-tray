# Agent Quota Tray

Claude Code ve Codex CLI'in 5 saatlik ve 7 gunluk kota yuzdelerini Windows gorev
cubugunda gosterir. Oturum acmaya gerek yoktur.

## Calistirma

    dotnet run --project src/AgentQuotaTray.App

## Test

    dotnet test

## Veri kaynaklari

- Claude: `GET https://api.anthropic.com/api/oauth/usage`
  (token `~/.claude/.credentials.json`, header `anthropic-beta: oauth-2025-04-20`)
- Codex: `codex app-server` uzerinden JSON-RPC `account/rateLimits/read`
  ve `account/rateLimits/updated`; yedek olarak `~/.codex/sessions/**/rollout-*.jsonl`

Ikisi de belgelenmemis arayuzlerdir. Kirilirlarsa uygulama tahmin uretmez,
"API degismis" gosterir.
