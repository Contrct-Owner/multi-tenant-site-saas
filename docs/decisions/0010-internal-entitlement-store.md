---
title: "Entitlement source of truth"
status: accepted
pinned: false
date: 2026-08-29
---

# 0010. Entitlement source of truth

## Decision

Plan definitions, org assignments, and exceptions live in our DB; evaluation never leaves the process. IEntitlementSource adapters (manual/admin first, billers later) push changes in via webhook.

## Why

Hot-path evaluation stays in-process; exceptions are first-class rows with expiry and reason.

## Consequences

We own drift reconciliation when a biller and our store disagree.
