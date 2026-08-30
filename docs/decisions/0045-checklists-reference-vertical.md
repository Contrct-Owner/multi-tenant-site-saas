---
title: "Checklists: the reference vertical, and the ops mobile answer"
status: accepted
pinned: false
date: 2026-08-30
---

# 0045. Checklists: the reference vertical, and the ops mobile answer

## Decision

The maintainer reopened the ops archetype's fork-territory line. Two pieces:

**Checklists module** - the ops core loop (daily per-site task lists), built
deliberately as the REFERENCE VERTICAL: scaffolded with
`tools/new-module.py`, consuming Tenancy only through a new `ISiteDirectory`
contract (scope path for gate 3, IANA zone for business dates), gated by two
new capabilities (`checklists:manage`, `checklists:complete`), RLS'd in its
first migration, tested through the same fixture as everything else. Scope
is minimal on purpose: templates apply daily (the archetype's overwhelming
case - opening/closing lists), item identity is positional, a check row per
(template, site, business date, item), and the business date is the SITE's
day (ADR 26 kind 3). Weekly/RRULE recurrence, assignments, photo proof, and
reporting are where a real ops fork differentiates.

**The mobile answer** is the responsive console as an installable PWA
(manifest + icon, no offline service worker on purpose - an ops tool that
silently serves stale data is worse than one that says it is offline). A
native field app remains fork territory.

## Why

Building the first vertical through the fork tooling is worth more than the
feature: this exercise found and fixed a scoped-service bug in the module
generator's DbContext template, a missing MigrationRunner step in its wiring
list, and a module-boundary arch test that had only ever registered one
module. The template's pitch is "forks extend it in an afternoon" - now that
path has been walked, and the potholes filled.

## Consequences

- `ISiteDirectory` is the sanctioned way for higher modules to hang
  site-scoped features off Tenancy; it returns path + zone, nothing more.
- The boundary arch test now covers every module - future modules must be
  registered there (the generator says so).
- Editing a template's items reorders positional identity; the honest fork
  upgrade is versioned templates, not index gymnastics.
