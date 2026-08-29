---
title: "Idempotency"
status: accepted
pinned: true
date: 2026-08-29
---

# 0029. Idempotency

## Decision

Idempotency-Key accepted on every unsafe HTTP method: server stores (key, org, endpoint, request fingerprint) with the response, replays on retry, 24h TTL, conflict on same key + different body. Beneath it, message-inbox dedupe for Wolverine's at-least-once delivery.

## Why

A uniform contract integrators can rely on; two distinct layers (HTTP and messaging).

## Consequences

Response-body storage and a cleanup job.
