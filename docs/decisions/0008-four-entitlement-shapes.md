---
title: "Entitlement shapes"
status: accepted
pinned: false
date: 2026-08-29
---

# 0008. Entitlement shapes

## Decision

Boolean capability, numeric limit, tiered value, and metered consumption are all supported.

## Why

Hierarchy depth is a numeric limit; audit retention is tiered; usage billing needs metering.

## Consequences

Metering is the expensive one: append-then-rollup events, approximate live counter for enforcement, period rollover, reconciliation.
