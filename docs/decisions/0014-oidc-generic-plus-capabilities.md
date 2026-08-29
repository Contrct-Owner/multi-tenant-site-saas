---
title: "Auth seam"
status: accepted
pinned: false
date: 2026-08-29
---

# 0014. Auth seam

## Decision

Any OIDC provider satisfies the base contract (authenticate, session, claims). Richer capabilities - per-org SSO, SCIM, admin portal, org switching - are optional interfaces feature-detected at startup. WorkOS implements all; a local provider implements the base.

## Why

Avoids shaping the interface around one vendor while keeping AuthKit's full value.

## Consequences

The app must degrade gracefully when a capability is absent.

Local development runs the REAL WorkOS adapter against the WorkOS emulator
(`@workos/emulate`, `ghcr.io/workos/emulate`, port 4100, key `sk_test_default`,
seeded from `workos-emulate.config.yaml`) - wired into the Aspire AppHost with
`--interactive` login pages. The integration suite smoke-tests the adapter
against the same emulator headless (non-interactive authorize auto-issues the
code). The local provider remains only for bare `dotnet run` and the
non-adapter test fixtures.
