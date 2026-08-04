---
paths:
  - src/ProcurementRadar.Parsers/**
  - tests/ProcurementRadar.Parsers.Tests/**
---

# Parser rules

`Parse(string body, Source source)` is a **pure function**. It may not:

- open a network connection (`HttpClient`, `WebClient`, `Playwright`)
- touch the database
- read `DateTime.Now`, `DateTime.UtcNow`, or the ambient time zone
- read files from disk
- throw on malformed input

Malformed rows are skipped and logged at `WARN`. One bad row must never
discard the rest of the page.

## Required output

`RawNotice.Title` and `RawNotice.SourceUrl` are mandatory. If either is
missing, skip the row.

`BidDeadlineAt` is best-effort but critical. Parse through `ITurkishDateParser`,
never with inline `DateTime.Parse`. If it comes back null, still emit the
notice — the notifier will flag it.

## Personal data

Strip person names and phone numbers before returning. Contact fields in
Turkish notices commonly appear as "İrtibat", "Bilgi için", "Tel". Mask them
here — nothing downstream will do it for you.

## Relative URLs

Always resolve against `adapter_config.base_url`. A stored relative link is
useless once the listing page changes.

## Fixtures

Every adapter needs at least one real HTML fixture captured from the live page.
Hand-written fake HTML is not accepted — it tests your assumption, not the site.

When a site changes and a parser breaks: add the new HTML as a second fixture,
keep the old one, make both pass. The archive still contains pages in the old
shape and reprocessing must work.
