---
title: "Cross-org users"
status: accepted
pinned: true
date: 2026-08-29
---

# 0005. Cross-org users

## Decision

A user may belong to multiple organizations. The session carries an active org; the principal (grants, scope, entitlements) re-resolves on switch.

## Why

Matches WorkOS AuthKit's native model; needed for franchise consultants, agencies, shared-services staff.

## Consequences

Org is never ambient: explicit on every cache key, job, and audit row.
