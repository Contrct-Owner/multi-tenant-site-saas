---
title: "Tenant context in background work"
status: accepted
pinned: false
date: 2026-08-29
---

# 0024. Tenant context in background work

## Decision

Tenant id rides the Wolverine message envelope; middleware materializes the principal and sets app.org_id on the connection so RLS makes omission fail closed. Cross-org scheduled work fans out: an enumerator enqueues one tenant-scoped message per org.

## Why

Three layers (envelope, middleware, RLS) and the framework does the propagation.

## Consequences

Many small messages instead of one big sweep - usually a feature.
