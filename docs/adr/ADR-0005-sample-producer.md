# ADR-0005: Sample producer

**Status:** Accepted  
**Date:** 2026-10-12

## Context

The spec diagram starts at "App → POST /v1/logs". A demo and a trace-page walkthrough need a visible, controllable log emitter; relying on manually curling the API makes the hub demo unrepeatable. NLog's `WebServiceTarget` could ship logs over HTTP, but it forces an awkward JSON envelope that doesn't match the ingest DTO shape.

## Decision

The demo load source is a tiny console app (`samples/LogPipe.SampleProducer`) inside the ingest repo:

- a plain `HttpClient` loop that fetches a client_credentials token (cached until expiry) and posts batches of generated entries (weighted levels, ~4 services, ~30% correlated bursts sharing a trace id);
- NLog stays where it shines — structured JSON console output of the producer's own operational logs.

## Consequences

- The producer doubles as living documentation of the REST contract; it is deliberately decoupled (own DTO copy, no project reference to `src/`) — drift is caught by the hub smoke tests, not by compilation.
- Self-shipping the producer's own logs back into the pipeline is out of scope (feedback-loop risk).
- Image `logpipe-sample-producer` (own Dockerfile, no contracts dependency) is scanned in CI and shipped by the hub compose.
