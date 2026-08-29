---
title: "Temporal model"
status: accepted
pinned: true
date: 2026-08-29
---

# 0026. Temporal model

## Decision

UTC instants store as timestamptz. Recurring local rules store as wall-clock time resolved against the site's IANA zone. Facts stamp the site-local business date at write time.

## Why

'Yesterday's numbers per store' becomes a plain group-by; same stamping pattern as ADR 2.

## Consequences

A site timezone change requires a restamp; developers must pick the right kind per column.
