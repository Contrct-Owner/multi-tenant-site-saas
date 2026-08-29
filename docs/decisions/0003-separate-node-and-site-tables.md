---
title: "Node vs site"
status: accepted
pinned: true
date: 2026-08-29
---

# 0003. Node vs site

## Decision

hierarchy_node holds the tree (path, level, org); site is its own entity referencing a parent node, carrying rich attributes (address, timezone, geo, external ids) plus a denormalized ltree path.

## Why

Sites get their own attribute surface, constraints, and indexes; scope stays one prefix predicate.

## Consequences

Two tables to join for 'everything under this node'.
