---
title: "Multiple hierarchies"
status: accepted
pinned: true
date: 2026-08-29
---

# 0004. Multiple hierarchies

## Decision

Nodes and site placements carry hierarchy_id from day one; v1 provisions exactly one tree per org and authz scope binds to it.

## Why

Retail orgs eventually want a second rollup (brand/format/franchise). A column now avoids a migration through every scope query and fact table later.

## Consequences

Fact path stamps must be keyed by hierarchy_id (child table or JSONB), never a single path column.
