# Procurement Radar

Turkish public-procurement notice scraper. Polls ~80 Ankara public-authority
pages plus EKAP, normalizes notices, scores them for a 3-person software team,
and pushes matches to Telegram within 15 minutes.

Full design: @docs/spec.md
Locked technical decisions: @docs/decisions.md
Work breakdown: @docs/tickets.md

## Language rule — non-negotiable

**English:** schema, tables, columns, enum values, classes, methods, variables,
files, folders, config keys, env vars, log messages, code comments, commit
messages, branch names.

**Turkish:** documentation under `docs/`, Telegram message text, keyword and
date-pattern data under `config/`.

**Never translate:** `EKAP`, `IKN`, `OKAS`, `KIK`, `SGK`. These are identifiers,
not words. `ikn` stays `ikn`.

## Stack

.NET 8 · Npgsql + Dapper (no EF Core) · DbUp migrations · AngleSharp ·
Playwright (only where JS rendering is required) · Serilog · Polly · xUnit ·
self-hosted PostgreSQL (Docker) · local filesystem archive · Telegram Bot API

**Zero-cost constraint.** No paid service, no cloud account, no API billing.
Everything runs on one machine the team already owns. If a task seems to
require a paid service, it is out of scope — say so instead of adding one.
There is no LLM in v1; see D-03.

## Commands

```
dotnet build
dotnet test
dotnet run --project src/ProcurementRadar.Migrator      # apply migrations
dotnet run --project src/ProcurementRadar.Worker        # run the worker
dotnet run --project src/ProcurementRadar.Cli -- seed   # load config/sources.seed.json
dotnet run --project src/ProcurementRadar.Cli -- reparse --source-id 12
```

## Architecture rules

1. **Collector never parses. Parser never touches the network.**
   `FetchAsync` returns a raw body and writes it to object storage.
   `Parse` is a pure function over that body — no `HttpClient`, no `DbContext`,
   no `DateTime.Now`. It must be unit-testable against a fixture file alone.
2. **`raw_fetch` rows are never deleted.** When a parser breaks we fix it and
   reprocess the archive. Losing the archive means losing the data permanently.
3. **`first_seen_at` is never recomputed.** It is the product's whole value.
4. Every adapter needs at least one fixture in
   `tests/ProcurementRadar.Parsers.Tests/fixtures/{adapter_key}/`.
   No fixture, no merge.
5. Keyword lists, date patterns and source definitions are **data**. They live in
   `config/*.json`. Never hardcode a Turkish string in a `.cs` file.
6. All timestamps are `timestamptz`, stored in UTC, handled as
   `DateTimeOffset`. Turkish source pages are `Europe/Istanbul` — convert on
   parse, never on read.

## HTTP politeness — hard requirements

- One in-flight request per host (`SemaphoreSlim` keyed by host)
- Minimum 5 s between requests to the same host, plus 0–3 s random jitter
- Send `If-None-Match` / `If-Modified-Since`; on 304 do not download the body
- `User-Agent: ProcurementRadar/1.0 (+mailto:CONTACT_EMAIL)`
- Respect `robots.txt`; record the check in `source.robots_checked_at`
- Between 02:00 and 05:00 Europe/Istanbul, triple `poll_interval_minutes`

Violating these gets our IP banned and kills the project. There is no speed to
be gained — a 15-minute interval is already far more than enough.

## Do not

- Do not add EF Core, MediatR, AutoMapper, or any ORM abstraction layer.
  Raw SQL through Dapper is the intended design.
- Do not store personal data. Mask contact names and phone numbers inside
  `Parse` before they ever reach the database.
- Do not invent source URLs. If `config/sources.seed.json` has a `TODO`, leave it.
- Do not write a Turkish identifier. If unsure of the English term, check the
  glossary in `docs/spec.md` section 1.
- Do not build UI, auth, or multi-tenancy. This is a single-team internal tool.
- Do not add any paid dependency, cloud SDK, or metered API. If you think one is
  needed, stop and ask.

## Definition of done

A ticket is done when: it builds, `dotnet test` passes, new parsers have
fixtures, no secret is committed, and the ticket's acceptance criteria in
`docs/tickets.md` are all checked off.
