---
title: "Deletion and restore"
status: accepted
pinned: true
date: 2026-08-29
---

# 0025. Deletion and restore

## Decision

Lifecycle status where the real world has one (a site closes or relocates - never 'deleted'; a membership ends; an org suspends). Soft delete with restore for user-generated content. Hard delete for join rows, tokens, ephemera. GDPR erasure remains a separate hard path regardless of tier.

## Why

Models reality instead of imposing one mechanism on it.

## Consequences

Every new entity must declare its tier; three patterns to document.
