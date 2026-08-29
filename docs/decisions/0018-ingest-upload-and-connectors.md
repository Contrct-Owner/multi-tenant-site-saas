---
title: "Site ingest"
status: accepted
pinned: false
date: 2026-08-29
---

# 0018. Site ingest

## Decision

Admin file upload (CSV/XLSX) plus pluggable pull connectors (ISiteSource), both over one core: staging tables, validation, dry-run diff preview, idempotent upsert via external-id mapping.

## Why

The diff preview is what makes bulk hierarchy re-parenting survivable. Closing a site is a domain event, never a delete.

## Consequences

Column-mapping UI, per-org connector credentials, per-connector failure handling.
