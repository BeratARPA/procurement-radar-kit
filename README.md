# Procurement Radar

**Watches Turkish public-procurement notices and sends the relevant ones to Telegram.**

Procurement Radar polls about 80 public-authority pages in Ankara plus EKAP, normalizes the notices, scores them against a small software team's profile and pushes matches to a Telegram group within 15 minutes. It runs on a single machine at zero cash cost: no cloud account, no paid API, no LLM in v1.

## How it works

```
sources.seed.json ─► Collector ─► Parsers ─► Persistence (PostgreSQL + file archive)
                                                  │
                                         Scoring ─┴─► Notifier ─► Telegram
```

| Project | Role |
|---|---|
| `ProcurementRadar.Collector` | Fetches source pages politely (rate limits, retries with Polly), archives every raw response |
| `ProcurementRadar.Parsers` | Per-source adapters (AngleSharp, Playwright only where JS is required) that turn pages into notices |
| `ProcurementRadar.Scoring` | Keyword and rule-based scoring (`config/keywords.json`) |
| `ProcurementRadar.Notifier` | Telegram messages (`config/tr-messages.json`) |
| `ProcurementRadar.Persistence` | Npgsql + Dapper, hand-written SQL |
| `ProcurementRadar.Migrator` | DbUp migrations |
| `ProcurementRadar.Worker` | The scheduled service that ties it together |
| `ProcurementRadar.Api` | Local API for the UI |
| `ProcurementRadar.Cli` | Tools such as trying a new source against its live page |
| `ui/` | Flutter client for the local API |

The archive of raw responses is the one thing that must never be lost: any parser can be fixed later and the archive re-processed.

## Stack

.NET 8 · PostgreSQL (Docker) · Npgsql + Dapper · DbUp · AngleSharp · Playwright · Serilog · Polly · xUnit · Telegram Bot API · Flutter (UI)

## Getting started

```bash
cp .env.example .env          # fill in the values; .env is gitignored
docker compose up -d          # local PostgreSQL
dotnet run --project src/ProcurementRadar.Migrator
dotnet run --project src/ProcurementRadar.Worker
dotnet test
```

Deployment as a Windows scheduled task or a Linux systemd service: [`docs/deployment.md`](docs/deployment.md) and [`deploy/`](deploy/).

## Documentation (Turkish)

- [`docs/spec.md`](docs/spec.md): full design
- [`docs/decisions.md`](docs/decisions.md): locked technical decisions
- [`docs/source-collection.md`](docs/source-collection.md): how sources were found and measured
- [`docs/tickets.md`](docs/tickets.md): work breakdown

## A note on the test fixtures

`tests/ProcurementRadar.Parsers.Tests/fixtures/` holds saved copies of public authority web pages, used only to test the parsers. They belong to their publishers.

## License

[MIT](LICENSE)
