# ADR-0003: Batch forwarder tradeoffs (best-effort with visible drops)

**Status:** Accepted  
**Date:** 2026-10-12

## Context

After the REST call returns 200, the entries must travel from ingest to search over gRPC. The einvoice family chose durability (RabbitMQ + outbox) because a lost invoice is money. A log pipeline has a different contract: losing logs under pressure is acceptable **if the loss is bounded, retried first, and observable** — blocking producers on a slow search would defeat the point of buffering and push latency back onto every emitting service.

## Decision

- Ingest buffers entries in a **bounded in-memory channel (10k, drop-newest when full)** — the REST path never blocks on the forwarder.
- `BatchForwarder` (hosted service) drains the channel in batches of `Batch:MaxSize` (100) / `Batch:MaxDelayMs` (500 ms) and sends them via gRPC client-streaming.
- Transient failures are retried up to `Batch:MaxAttempts` (3) with exponential backoff (200/400/800 ms); a batch that exhausts retries is **dropped** and counted in `logpipe_ingest_entries_dropped_total`.

## Consequences

- Entries can be lost on ingest crash or a prolonged search outage — the production fix would be a durable buffer (Redis Streams/Kafka) and is on the roadmap, not in F2.
- Drops are visible: Prometheus exposes `logpipe_ingest_entries_dropped_total`, so silent data loss is not possible — loss is a metric, not a mystery.
- Dedup at search (entry id) keeps retries idempotent; the gRPC client (`ILogBatchClient`) is an interface so tests drive `BatchForwarder` with a recording fake (no live gRPC in unit tests).
