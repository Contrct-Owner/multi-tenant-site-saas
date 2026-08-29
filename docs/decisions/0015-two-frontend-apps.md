---
title: "Frontend topology"
status: accepted
pinned: false
date: 2026-08-29
---

# 0015. Frontend topology

## Decision

Two apps in one workspace: TanStack Start (SSR) for the public/guest surface, pure SPA (Router + Query) for the console. Shared ui package, generated API client, generated capability keys.

## Why

Public surfaces need SEO and first paint; the console does not need SSR complexity.

## Consequences

Two builds and deploy targets; shared-package discipline.
