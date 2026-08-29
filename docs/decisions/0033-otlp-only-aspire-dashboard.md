---
title: "Telemetry"
status: accepted
pinned: false
date: 2026-08-29
---

# 0033. Telemetry

## Decision

The application exports OTLP exclusively; local development uses the standalone Aspire dashboard as the sink. Tenant/site/actor ride traces and logs as baggage - never as metric labels.

## Why

Vendor-neutral; forks point OTLP anywhere. Cardinality on metrics would wreck the bill.

## Consequences

Redaction and tail sampling are application concerns (no collector shipped by default).
