---
title: "Org-defined site attributes"
status: accepted
pinned: false
date: 2026-08-30
---

# 0046. Org-defined site attributes

## Decision

Sites carry two kinds of extension, and the template now supports both:
FORKS add columns (a migration, reviewed like code); TENANTS define
attributes at runtime. Definitions are org data (`site_attribute_definitions`,
RLS'd): a stable slug key, a human label, a type from a closed set (Text,
Number, Boolean), and a Public flag. Values live in a `jsonb attributes`
column on the site row - one fetch, RLS'd with the site, exported naturally.
No EAV tables.

Writes validate against the definitions (unknown key and wrong type are
400s; null removes; patch-merge preserves untouched keys). Deleting a
definition strips its values from every site in the same transaction - an
orphaned key in jsonb is schema debt nobody can see. The Public flag is the
visibility gate: the public site page renders public attributes (with
labels); the listings feed carries everything, because connectors are
org-side.

## Why

Multi-location orgs are heterogeneous in ways no fork can predict - one
tenant needs a drive-thru flag, another a cost center, a third a manager
name - and every product in the category (Yext custom fields, FranConnect's
field builder) treats this as table stakes. It is the same move as
org-defined roles: the data model itself becomes tenant data. And every
existing surface compounds it: the feed hands attributes to connectors, the
public page displays them, org export includes them.

## Consequences

- The closed type set is deliberate; Select/enum types, validation rules,
  and per-node attributes are the fork's field-builder territory.
- Ingest does not yet map attributes; the natural rule (unknown CSV columns
  with a matching definition key become attribute values) is its own slice
  because the staging diff machinery must learn them too.
- Definition keys are API surface for that org: renaming means delete +
  recreate, and values go with the delete. Forks wanting rename add key
  migration explicitly.
