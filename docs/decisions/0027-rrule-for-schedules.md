---
title: "Recurrence"
status: accepted
pinned: true
date: 2026-08-29
---

# 0027. Recurrence

## Decision

Schedule-like data (operating hours, closures, special hours) uses RFC 5545 RRULE with EXDATE/RDATE. DTSTART carries a TZID; expansion is server-authoritative in the site's zone, converted to instants only after expansion.

## Why

RRULE is the correct primitive and brings exception dates for free; client libs disagree with server libs at the edges, so the server expands and the client displays.

## Consequences

A fourth temporal kind alongside ADR 26's three.
