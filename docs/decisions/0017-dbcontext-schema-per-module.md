---
title: "Persistence boundaries"
status: accepted
pinned: true
date: 2026-08-29
---

# 0017. Persistence boundaries

## Decision

Each module owns a Postgres schema, its own DbContext, and its own migration history - one database.

## Why

Cross-module joins become physically impossible; the boundary holds under deadline pressure; extraction to a service later is mechanical. One database keeps audit interceptors transactional.

## Consequences

No cross-module transactions: spanning work goes through Wolverine messages and the outbox.
