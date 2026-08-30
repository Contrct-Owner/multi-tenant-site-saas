---
title: "Support impersonation"
status: accepted
pinned: false
date: 2026-08-30
---

# 0042. Support impersonation

## Decision

An operator can open a **time-boxed support session into a tenant org**:
`POST /api/operator/orgs/{id}/impersonate` reissues the operator's own cookie
with the target as active org plus an expiry claim. No new principal tier, no
synthetic membership, no database state:

- `Principal.User` carries `ImpersonationExpiresAt`; the accessor sets it only
  while the claim is unexpired (a pure clock read - the accessor still never
  touches the database).
- The scope resolver gives an impersonating principal **org-wide scope on
  every capability except the `platform` domain**. Owner-equivalent is what
  support needs; stripping platform reach means an impersonation cookie can
  never operate the platform, and `IsOperatorAsync` fails during impersonation
  (active org is not the platform org), so sessions cannot chain or reach
  other tenants.
- The session row is the operator's own: revoking their session (or
  suspending them) kills the impersonation with it. Expiry needs no cleanup -
  the claim simply stops mattering, leaving the cookie pointing at an org
  with no membership, which resolves to nothing.
- Both start and stop are **domain-audited into the TARGET org** with the
  operator as actor - the tenant's own audit page shows support was in their
  org. Expiry and org-switch end a session without an `ended` event; the
  `started` event carries the expiry, so the window is always on record.
- The console shows a persistent banner (org name, time left, stop button)
  whenever `/me` reports impersonation.

Guards: operator-only, the platform org itself cannot be impersonated, and
TTL comes from `Impersonation:TtlSeconds` (default 3600).

## Why

Support needs to see what the tenant sees; screenshots and guesswork are
worse for privacy than an audited, time-boxed session. Claims-only fits the
read-time tenancy rule (nothing to "set" anywhere), and reusing the
operator's session means every existing control - session revocation,
suspension, the operator wall - applies without new machinery.

## Consequences

- Endpoints that check raw MEMBERSHIP rather than grants (leave, contact-link
  issuance) fail closed during impersonation - deliberately: support has no
  business leaving an org it is not in or minting invitations as the tenant.
- RLS sees the target org (tenant context reads the active-org claim), so
  impersonated reads are exactly the tenant's rows - no cross-org bleed to
  debug twice.
- A suspended target stays suspended: the suspension middleware blocks by
  active org before anything else, impersonation included.
