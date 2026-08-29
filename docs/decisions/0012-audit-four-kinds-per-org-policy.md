---
title: "Audit capture"
status: accepted
pinned: false
date: 2026-08-29
---

# 0012. Audit capture

## Decision

Domain events (intent), authorization decisions (with reason), row-level change diffs (EF interceptor), and read/access logging - with per-org policy resolved as entitlement ceiling intersected with app config.

## Why

Each kind answers a different question; configurability is itself a sellable entitlement.

## Consequences

A platform floor no tenant can configure below; audit-config changes are themselves audited; diffs need field-level PII redaction.
