# ADR-0002: Rate limit and dedup on the ingest path

**Status:** Accepted  
**Date:** 2026-10-12

## Context

The spec diagram puts "Redis: rate limit/dedup" on the ingest hop. Producers retrying on 5xx, or agents double-shipping logs, must not inflate the search index; and a single noisy client must not exhaust the ingest→search forwarder. Ingest already needs Redis (rate limiting was always in scope), so dedup costs nothing extra in infrastructure.

## Decision

- **Rate limit:** per-client **fixed one-minute window** (`RateLimit:EntriesPerMinute`, default 1200) enforced on the whole request — a batch that would overflow the window gets a single 429 (`rate_limit_exceeded`), implemented as one Redis `INCR` key per `client:window` with a 2-minute TTL (covers minute-boundary skew).
- **Dedup:** Redis `SET NX` with 1h TTL keyed by entry `id`; when the client omits the id, the key is a SHA-256 of `timestamp|service|message`. Duplicates count as `Rejected` in the response and in the `logpipe_ingest_entries_rejected_total` counter.

## Consequences

- Duplicates still consume rate-limit budget (cheap, simple accounting; no second Redis round-trip to refund).
- Dedup state survives ingest restarts only insofar as Redis holds it — the demo compose has no Redis volume, accepted for a study project.
- Whole-request 429 keeps client retry semantics trivial: back off and resend the batch (dedup absorbs any overlap with what already landed).
- The dedup decision must be made with the **client-provided** id (or its absence) — ingest assigns a synthetic GUID for the wire, which must not become the dedup key (fixed during implementation; see `LogIngestService` tuple carrying `IdFromClient`).
