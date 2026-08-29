---
title: "Hierarchy in time"
status: accepted
pinned: true
date: 2026-08-29
---

# 0002. Hierarchy in time

## Decision

The hierarchy is current-only; fact/transactional rows stamp the ancestor path (as of write time) so historical rollups reflect the structure as it was.

## Why

Correct as-was reporting after re-parenting at ~10% of the cost of bitemporal edges. Standard retail-analytics pattern.

## Consequences

Restating history after a bad re-parent requires a backfill job. Stamps must be tree-keyed (see ADR 4).
