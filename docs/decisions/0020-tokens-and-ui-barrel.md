---
title: "Styling"
status: accepted
pinned: false
date: 2026-08-29
---

# 0020. Styling

## Decision

Tailwind v4 CSS-first @theme owns all design tokens; registry resolution prefers ReUI then shadcn; app code imports only from @/ui, never components/ui/* directly.

## Why

A real seam for reskin/replace without pretending copy-in components are pluggable.

## Consequences

A lint rule enforces the barrel.
