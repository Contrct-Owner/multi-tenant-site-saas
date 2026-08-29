---
title: "Fork model"
status: accepted
pinned: false
date: 2026-08-29
---

# 0036. Fork model

## Decision

GitHub template repo with an init script (renames namespaces, sets product name, generates keys, removes samples) and a module generator (schema, DbContext, migration history, Wolverine registration, arch-test registration, test fixtures). No upstream merge story - stated plainly.

## Why

The generator matters because every module needs its own schema and migration history (ADR 17). .claude/ and CLAUDE.md are product surface forks inherit.

## Consequences

Platform fixes never reach existing forks.
