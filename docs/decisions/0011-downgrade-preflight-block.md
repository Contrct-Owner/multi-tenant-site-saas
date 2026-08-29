---
title: "Entitlement downgrade"
status: accepted
pinned: false
date: 2026-08-29
---

# 0011. Entitlement downgrade

## Decision

Shrinking an entitlement below current usage runs a preflight conformance check reporting exactly what is over; the change is refused until an admin remediates.

## Why

Keeps 'degraded org' out of every other module's vocabulary.

## Consequences

Manual cleanup; sales occasionally annoyed.
