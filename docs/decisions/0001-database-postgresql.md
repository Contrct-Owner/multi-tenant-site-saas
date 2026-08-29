---
title: "PostgreSQL"
status: accepted
pinned: true
date: 2026-08-29
---

# 0001. PostgreSQL

## Decision

PostgreSQL is the only supported database engine.

## Why

Three pillars the design depends on are native: row-level security for fail-closed tenant isolation, ltree for materialized-path hierarchy scope queries, and PostGIS for site proximity search. Best EF Core provider outside SQL Server.

## Consequences

Forks are committed to Postgres. Engine-specific bits (path queries, RLS setup, geo) stay in clearly marked places.
