---
title: "Rate limiting"
status: accepted
pinned: false
date: 2026-08-29
---

# 0030. Rate limiting

## Decision

.NET's built-in rate limiter partitioned by principal tier: strict per guest session (IP fallback), looser per user, per-org quota reading metered entitlements. In-process by default behind an abstraction; Redis-backed when a fork scales out.

## Why

The guest session cookie (ADR 7/21) is a better limit subject than IP.

## Consequences

Optional Redis dependency; per-fork tuning.
