---
title: "Data residency"
status: accepted
pinned: true
date: 2026-08-29
---

# 0035. Data residency

## Decision

v1 runs regional silos (org record names its region; a spanning customer is two orgs). Full multi-region routing is a committed later step, so six preconditions hold from the first commit: no ambient connection string; org-to-region resolves outside the regional DB; UUIDv7 keys; region in storage buckets/keys; region on cache keys; identity global while org data is regional.

## Why

Honest and cheap now; the preconditions keep step two an addition instead of a rewrite.

## Consequences

No global admin view across silos in v1.
