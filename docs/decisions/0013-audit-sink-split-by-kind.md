---
title: "Audit sink"
status: accepted
pinned: false
date: 2026-08-29
---

# 0013. Audit sink

## Decision

Write and authz audit commit in the same transaction as the change, then fan out via outbox to external sinks. Read/access audit goes async to a partitioned store.

## Why

Transactional integrity where compliance needs it; the primary DB doesn't drown in read logging.

## Consequences

Two paths, two retention policies, two query surfaces.
