---
title: "Topology"
status: accepted
pinned: false
date: 2026-08-29
---

# 0034. Topology

## Decision

Aspire AppHost orchestrates local dev (Postgres, storage emulator, mail catcher, dashboard). Deployment is one OCI image run as api or worker via a role flag.

## Why

Single-command local start; web and background capacity scale independently without a second build.

## Consequences

Keep the AppHost honest as modules are added.
