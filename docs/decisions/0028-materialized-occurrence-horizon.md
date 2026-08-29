---
title: "Recurrence storage"
status: accepted
pinned: false
date: 2026-08-29
---

# 0028. Recurrence storage

## Decision

The rule + exceptions is stored truth; a Wolverine job expands occurrences into an indexed table over a rolling horizon (~12 months), refreshed on rule change and on schedule.

## Why

An RRULE cannot be indexed; 'which of 3,000 sites are open now' must be a range query.

## Consequences

Explicit rebuild triggers: rule edits, exception changes, horizon roll, and - easiest to forget - a site timezone change.
