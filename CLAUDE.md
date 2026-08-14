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
dotnet run --project src/ProcurementRadar.Cli -- rescore --dry-run  # score the archive with the stored keyword list (D-21)
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

## The tests do not live where the worker lives

Every production breakage in this project so far has come from one gap: **what a
test sees is not what the worker gets.** Not one of them was a logic error.

| The test sees | The worker gets | What it cost |
|---|---|---|
| a fake repository behind an interface | Npgsql and Dapper | a record materialized fine in tests and threw at runtime, twice |
| a config file | a row in `setting` | the deadline hour stayed 23:59 for two hours; an alarm shipped with no message text |
| a hand-written parameter list | a statement PostgreSQL parses | a missing parameter killed every parse run for two hours |
| the repository working copy | `publish/config` | settings were seeded from a stale copy |
| the working copy on this disk | what a clone actually contains | `.gitignore`'s `data/` matched `ui/lib/src/data/`; four files the interface cannot start without were never committed, and `git add -A` never said a word |
| an exception during layout | content quietly cut off at the window edge | a Row inside a horizontal scroll view never overflows, so three of the five notice filters sat off screen with nothing saying they existed |
| a stub adapter standing in for the whole registry | a registry the command builds by hand, with one adapter in it | `reparse` skipped every EKAP and json_list row for a year. The archive exists so a fixed parser can reprocess it; the parser the spec calls most fragile was the one that rule could not reach |
| the schema the client was generated from | the schema the api now serves | `ActiveChangeRequest.reason` was added and the client was not regenerated; the field is optional, so no request failed - the api read the absent reason as `manual` and every source switched off from the interface opted out of quarantine for two days |

So, before saying a change works:

1. **Anything that touches SQL runs against the real database once.** A green
   unit test with a fake behind it proves the code, not the driver. `dotnet run
   --project src/ProcurementRadar.Cli -- preflight` does this in ten seconds.
2. **Anything that changes `config/*.json` is pushed to the `setting` table.**
   The file is only the seed (D-16); editing it changes nothing on a machine
   that has already run. Push it through the api, do not edit the row by hand -
   the api validates it and keeps the previous value.
3. **Read the log after deploying, not before.** Every fault above was already
   in it, saying exactly what was wrong, for hours.
4. **Anything that adds files runs once from a fresh clone.** `git clone` to a
   scratch directory, then `dotnet build` and `flutter test`. A file this disk
   has and the repository does not is invisible from here and total from there.
5. **Anything that changes the api regenerates the Dart client in the same
   commit.** `dart run swagger_parser && dart run build_runner build` in `ui/`.
   `preflight` compares `ui/swagger.json` against the running api and names what
   is missing, but only if the api is up when it runs.

`dotnet test` passing is necessary and has never once been sufficient.

## Definition of done

A ticket is done when: it builds, `dotnet test` passes, **`preflight` passes
against the real database**, new parsers have fixtures, changed `config/*.json`
has been pushed to the `setting` table, no secret is committed, and the ticket's
acceptance criteria in `docs/tickets.md` are all checked off.
