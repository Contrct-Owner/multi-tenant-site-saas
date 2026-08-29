---
title: "API contract"
status: accepted
pinned: false
date: 2026-08-29
---

# 0016. API contract

## Decision

.NET 10 native OpenAPI 3.1 generates the TS client and TanStack Query hooks. The same pipeline generates permission and entitlement keys into C# constants and TS union types.

## Why

Capability strings cannot drift between two languages.

## Consequences

The spec is a reviewed artifact; CI asserts regenerated output is diff-clean.
