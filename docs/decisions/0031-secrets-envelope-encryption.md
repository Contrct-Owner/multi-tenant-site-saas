---
title: "Connector credentials"
status: accepted
pinned: false
date: 2026-08-29
---

# 0031. Connector credentials

## Decision

Per-org connector credentials envelope-encrypted in Postgres: data key wrapped by a pluggable cloud KMS (AWS KMS, Azure Key Vault, local dev provider). Credential access is audited.

## Why

Secrets stay joined to connector rows with one round trip; forks choose their KMS.

## Consequences

We own rotation/re-wrapping; the local provider must be unmistakably non-production.
