---
title: "Limit behavior"
status: accepted
pinned: false
date: 2026-08-29
---

# 0009. Limit behavior

## Decision

Each entitlement definition declares one policy from a closed set: Block, Grace, Overage, or WarnOnly.

## Why

'Max sites' and 'API calls this month' must not behave alike.

## Consequences

Four enforcement paths to implement and test.
