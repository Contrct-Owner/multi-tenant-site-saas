---
title: "Permission model"
status: accepted
pinned: true
date: 2026-08-29
---

# 0006. Permission model

## Decision

Admins assign roles at a scope; roles compile to (domain, action) grants. Individual time-boxed exception grants (with reason, expiry, granting actor) can be added on top. No deny rules.

## Why

Monotonic evaluation - more grants only ever means more access - keeps 'why can this person do that?' answerable.

## Consequences

Carving an exception OUT of a role means splitting the role, not denying.
