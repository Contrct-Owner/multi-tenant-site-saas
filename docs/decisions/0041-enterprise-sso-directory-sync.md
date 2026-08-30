---
title: "Enterprise SSO self-service and directory sync"
status: accepted
pinned: false
date: 2026-08-30
---

# 0041. Enterprise SSO self-service and directory sync

## Decision

Two new optional capabilities on the auth seam (ADR 14), feature-detected like
the rest:

- **`IAdminPortal`** - the provider hosts the IT-admin configuration UI for
  SSO connections and directory sync. We generate a short-lived portal link
  scoped to the org's external id and redirect; connection setup, IdP metadata,
  and SCIM credentials never touch our code (same shape as ADR 39's hosted
  billing pages).
- **`IDirectoryEventSource`** - the provider's directory-sync webhook, parsed
  behind the seam (framework-neutral body + headers, provider-verified
  signature) into a neutral event: user upserted or user removed, keyed by
  **email** and the provider-side org id.

The webhook endpoint maps the external org id through Identity's own
`org_directory` read model (ADR 37 - no Tenancy contract) and publishes an
envelope-tenanted message. The handler:

- **Upsert (active)**: ensure the provider-side user exists, ensure the local
  user + membership. Role assignment stays internal (ADR 6) - a recorded
  invited role wins; otherwise the member arrives with no grants and an admin
  assigns roles in the role editor. Directory groups do NOT map to roles in
  the template; that mapping is fork territory.
- **Remove (deleted or deactivated)**: delete the membership and its roles
  (tier 3, ADR 25) and revoke ALL of the user's server-side sessions. A
  multi-org user gets logged out everywhere and signs back into their other
  orgs; deprovisioning is rare and security wins over convenience. The IdP's
  word is final: removal proceeds even if it orphans role management (an
  operator can repair an orphaned org; a lingering account cannot be repaired
  by anyone but us).

Gating: the portal endpoints require the `org:manage` grant and the new
`sso.enabled` boolean entitlement (free/Growth: off, Scale: on) - failing
entitlement is a 402 upsell (ADR 8). The webhook is anonymous by nature and
authenticated by the provider's signature; unverifiable deliveries 400,
verified-but-irrelevant event types 202 so provider retry health stays green.

## Why

SSO + SCIM is the classic enterprise-tier gate, and WorkOS's whole pitch is
that the portal and directory plumbing are theirs. Owning only the two seam
calls (make a link, parse an event) keeps the template vendor-portable: any
future provider slots in behind the same two interfaces, and the local dev
provider simply lacks them (the console shows the feature as unavailable).

Provisioning keys on email, not provider subject: SCIM events carry directory
user ids, while login carries user-management ids - email is the only stable
join, and is already the invitation join key.

## Consequences

- JIT membership at SSO login (ADR 14) already handled *provisioning*; what
  directory sync truly adds is **deprovisioning** - removal at the IdP now
  revokes access here without an admin remembering to.
- `IUserProvisioning.EnsureUserAsync` returns the provider-side user id, so
  directory provisioning can create the local user before first login.
- The WorkOS emulator serves portal-link generation (smoke-tested through the
  real adapter) but not the portal PAGE itself (it 401s), and it cannot emit
  dsync webhooks - so webhook tests hand-sign WorkOS-format payloads against
  the real verification path (the same approach ADR 39 uses for Stripe), and
  walking the portal UI end-to-end needs a real WorkOS environment.
- With no `Auth:WorkOS:WebhookSecret` configured (the dev default), webhook
  deliveries are rejected unverified - consumption is opt-in and fail-closed.
