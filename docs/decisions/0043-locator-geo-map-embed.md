---
title: "Locator: geo search, map, embed"
status: accepted
pinned: false
date: 2026-08-30
---

# 0043. Locator: geo search, map, embed

## Decision

The maintainer reopened the locator's fork-territory line (competitive
review): the template now ships the locator archetype's three table-stakes
features, in their vendor-free form.

- **Geo search**: `/public/sites?near=lat,lng` sorts by great-circle
  distance and returns `distanceKm`; sites without coordinates list last
  rather than vanishing. Haversine runs in memory - the public fleet list is
  unpaged and modest by design, and SQL translation risk buys nothing at
  this size. Coordinates are editable in the site dialog (range-checked,
  pair-required).
- **Map**: Leaflet over OpenStreetMap tiles - no API key, no vendor
  account, works the moment a fork deploys. Circle markers in the brand
  color on purpose: Leaflet's default icon assets fight every bundler.
  Client-only component; SSR renders the list and the map hydrates in.
- **Embed**: `/embed` is the locator without chrome, meant for an iframe on
  the org's own website; site links open the full page in a new tab. The
  console's Settings page hands out the snippet via `GET
  /api/org/public-url` - the API is the one place that knows
  `Public:HostTemplate`.

## Why

The competitive review's verdict: a locator fork is "closest to sellable,"
needing exactly these three. Each has a form that requires no vendor
relationship, so they belong in the template; what stays fork territory is
what does: commercial tile servers at volume (OSM's usage policy covers
development and small deployments), geocoding addresses to coordinates
(every provider is keyed), and locator analytics.

## Consequences

- The embed page is meant to be framed, so the public app must never grow a
  blanket `X-Frame-Options` - the API's deny-all headers are its own.
- `distanceKm` is great-circle, not driving distance - honest for "which is
  closest," wrong for routing; forks that need routing need a provider.
- Coordinates are hand-entered or ingested; a geocoding connector is the
  natural first ingest extension for a locator fork.
