# ADR-0001: OIDC issuer with SQLite store

**Status:** Accepted  
**Date:** 2026-10-12

## Context

`logpipe-ingest` must issue bearer tokens for the family (writers post logs, readers/search call APIs), and `logpipe-search` must validate them. OpenIddict 7 removed its in-memory stores (verified in the 7.7.2 package — only store interfaces remain), so an issuer needs *some* persistent store. The F2 spec stack has no database; dragging Postgres in just for two demo clients would dwarf the family budget.

## Decision

Ingest embeds an OpenIddict **client_credentials** server:

- scopes `logs:read` / `logs:write`; clients `logpipe-writer` / `logpipe-reader` sharing one demo secret from env (`Oidc:ClientSecret`);
- store = **EF Core + a SQLite file** (`Oidc:DbPath`), self-contained, no server process;
- validation is local (`UseLocalServer`) inside ingest itself;
- the issuer URL is explicit (`Oidc:Issuer`) so host-local and containerized runs produce consistent `iss` claims;
- non-Development boot **fails fast** if `Oidc:ClientSecret` is empty (no accidental world-writable demo secret).

## Consequences

- The SQLite file is ephemeral — reseeded every boot, fine for a demo; production would swap to Postgres (same managers, one EF provider line).
- Tokens are standard JWTs signed by the OpenIddict development certificate (regenerated per host; 5-minute TTL makes rotation harmless).
- `logpipe-search` validates tokens through standard OIDC discovery (JwtBearer `Authority` = the same URL) — no shared secret, no custom middleware (see search ADR-0002).
