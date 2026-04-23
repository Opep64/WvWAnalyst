# Prototype Architecture

## Guiding split

- `Elite Insights Parser`
  Owns canonical one-fight truth and any fight-local derived metrics
- `WvWAnalyst`
  Owns local cataloging, trend analysis, scorecards, storage management, and the analyst-facing UI

The prototype initially avoided touching the parser until the new app's local workflow was proven.
That boundary has now started to relax: a first-pass parser-side `analysis.json` export is being introduced so single-fight derived metrics still live with the parser rather than being recomputed in `WvWAnalyst`.

## Local-first shape

`WvWAnalyst` currently runs as a single local web process:

- `ASP.NET Core` serves the dashboard UI
- minimal JSON endpoints feed the same UI
- storage lives under `storage/`
- parser and combiner repos are detected as local neighbors

## Future evolution

The intended long-term path is:

1. add parser orchestration from the local app
2. ingest parser-produced canonical fight-analysis payloads
3. expand the compact manifest/index into a richer catalog and SQLite-backed metadata
4. add trend analytics and log retention tooling
5. move the same product shape to a server-backed deployment

## Canonical v1 fight team scorecard

The prototype encodes the agreed v1 blueprint:

- non-scored context
- non-scored outcome headline
- 4 primary score pillars
- supporting explanation metrics kept outside the score

The scorecard definition currently lives in `packages/analytics` so it can be reused by both the API and future UI/report projections.
