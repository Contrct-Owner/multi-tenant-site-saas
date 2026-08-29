---
title: "Job runtime and messaging"
status: accepted
pinned: true
date: 2026-08-29
---

# 0023. Job runtime and messaging

## Decision

Wolverine provides in-process mediation for slice handlers, cross-module integration messaging, and the durable Postgres-backed outbox with scheduled messages. No separate mediator library.

## Why

One dependency serves ADR 13's outbox and ADR 17's module boundary. License verified 2026-08-29: core is MIT through current 6.x; JasperFx's commercial line is CritterWatch only (BSL, optional tooling forks never inherit).

## Consequences

Concentration of risk: a Wolverine problem is simultaneously a handler, messaging, and audit problem. Management UI beyond read-only CritterWatch = the OTel path (ADR 33).
