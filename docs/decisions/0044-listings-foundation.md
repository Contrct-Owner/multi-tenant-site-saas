---
title: "Listings foundation: canonical feed + change webhooks"
status: accepted
pinned: false
date: 2026-08-30
---

# 0044. Listings foundation: canonical feed + change webhooks

## Decision

The maintainer reopened the listings archetype's fork-territory line. The
template ships the *foundation*, not the connectors:

- `GET /api/listings/feed` - every site in the caller's scope as a full
  listing record: identity, address, coordinates, status, public URL, and
  hours as RULES (RRULE + exception dates), which is the shape providers
  consume. Built for API-key access (ADR 40); a subtree-scoped key exports
  its subtree.
- Change notification is the EXISTING outbound webhook system - `site.*`
  events, signed, retried. No parallel push pipeline.
- `docs/listings-connectors.md` documents the connector loop (pull-on-boot,
  webhook-triggered re-pull-and-diff, conservative status mapping).

An `IListingsPublisher` push seam was considered and rejected: every real
target needs its own OAuth relationship, verification flow, and field
mapping - a seam with no second implementation in the template is
speculation, and the feed + webhooks pair already carries a connector
written OUTSIDE the codebase (a partner, a Zapier-style tool, a fork).

## Why

The competitive review placed the entire listings value proposition
(Google/Apple/Bing sync) in real, grinding, provider-specific integration
work. What the template can honestly guarantee is that no connector ever
reverse-engineers site truth: one canonical record, one signal for change.

## Consequences

- The feed is the contract: additive changes only once forks consume it.
- Feed hours are rules, not windows - a consumer that wants materialized
  windows uses the public site endpoints instead.
- Deleting the fork-territory line does not delete the fork work: the
  connector itself (OAuth app, provider verification, id mapping) remains
  the fork's, by design.
