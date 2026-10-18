# ADR-0004: Observability baseline (NLog JSON + OTel Prometheus)

**Status:** Accepted  
**Date:** 2026-10-12

## Context

The logpipe family's whole point is observability, so the services must eat their own dog food from day one. The spec (§5) lists OTel, Prometheus and NLog as F2's focus, but a full collector stack (OTLP collector + Jaeger + agents) would dwarf the family's budget and local footprint. The einvoice family already proved the pattern: structured JSON logs to console + Prometheus scraped metrics.

## Decision

Every logpipe service:

- logs through **NLog** with a JSON `JsonLayout` on console (`nlog.config`, `throwConfigExceptions=true` so bad layout renderers fail at startup, not at 3 a.m.);
- exposes **OpenTelemetry** metrics via the Prometheus exporter at `/metrics`;
- carries traces in-process only; the **trace id is data**, flowing as the `trace_id` field of log documents — the UI's trace page correlates entries by it (no collector/Jaeger in scope).

## Consequences

- Prometheus (shipped in the compose hub) is the live metrics UI; each service adds `AddMeter("LogPipe.Ingest")`-style registrations so family counters (`logpipe_ingest_*`) appear without extra wiring.
- "Distributed tracing" is demonstrated by correlated log entries sharing a W3C trace id — viewable in logpipe-ui's trace page — not by a span waterfall.
- Adding an OTLP collector later is a config-only change (add `AddOtlpExporter()`), because the OTel SDK is already the logging/metrics facade.
