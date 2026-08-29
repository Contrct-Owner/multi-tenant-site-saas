# Premise

A forkable template for location/site-based multi-tenant SaaS.

A vertically sliced modular monolith. C# 14 / .NET 10 / EF Core 10 / PostgreSQL backend; TanStack +
TypeScript frontend (Start for the public app, SPA for the console); WorkOS behind
an OIDC-generic auth seam; Wolverine for mediation, messaging, and the outbox.

**Tenancy has two axes**: ownership (which org owns a row) and scope (which site or
subtree it belongs to). Every request passes **three gates**, in order:

1. **Entitlement** — does the org's plan include this capability? Fails as 402/upsell.
2. **Grant** — does the principal hold `(domain, action)`? Fails as 403.
3. **Scope** — over which hierarchy nodes does the grant apply? Never fails — it
   *filters*. `scopeFor(principal, action)` returns a `NodeScope` that repositories
   require as an argument.

## Architectural decisions

All 36 settled decisions live in `docs/decisions/` (one ADR each, indexed in its
README). **Consult them before proposing structural changes.** Decisions marked
`pinned: true` are expensive to reverse once data exists — do not contradict them
without the maintainer explicitly reopening the decision.

## Invariants that cannot be automated (so hold them yourself)

Rules below are judgment calls a compiler can't catch. Everything mechanical is
enforced by analyzers, architecture tests, RLS, and CI — trust those layers and
don't restate them here.

- **Org is never ambient.** Org id (and region) appear explicitly on every cache
  key, message envelope, background job, and audit record. If you write code that
  assumes "the current org," you are wrong — resolve it from the principal or the
  envelope.
- **No ambient connection string.** All data access resolves its context from the
  org's region, even while there is only one region (ADR 35).
- **Every new entity declares its deletion tier** (ADR 25): lifecycle status
  (sites, orgs, memberships), soft-delete with restore (user content), or hard
  delete (join rows, tokens, ephemera). A site is *closed*, never deleted.
- **Every temporal column is one of four kinds** (ADR 26/27): UTC instant
  (`timestamptz`), wall-clock recurring rule (RRULE with TZID), stamped site-local
  business date, or materialized occurrence. Name and comment which one it is.
- **Fact tables stamp context at write time**: ancestor path keyed by
  `hierarchy_id` (ADR 02/04) and site-local business date (ADR 26).
- **RRULE expansion is server-authoritative** and happens in the site's IANA zone;
  the client only displays (ADR 27).
- **New tenant-scoped tables need an RLS policy** in the same migration. CI asserts
  coverage, but write it up front — use the `new-migration` skill.
- **Keys are UUIDv7**, never database sequences (ADR 35 preconditions).
- **Never put tenant/site/actor on metric labels** — traces and logs only, as
  baggage (ADR 33).
- **Frontend imports UI only from `@/ui`**, never `components/ui/*` directly
  (ADR 20). Capability keys come from codegen, never hand-typed strings (ADR 16).
- **Guests are principals.** No anonymous code paths — the principal pipeline
  builds a tenant-scoped Guest from the request host before authn (ADR 07).

## Workflows

- New vertical slice / module: use the **new-module** skill. Never hand-roll a
  module; each one needs its own schema, DbContext, migration history, Wolverine
  registration, arch-test registration, and test fixtures.
- New EF migration: use the **new-migration** skill (carries the RLS checklist).
- Applied migrations are immutable — add a new migration instead of editing one
  (a hook enforces this).

## Commands

- Build: `dotnet build Premise.slnx` (Aspire CLI must be on PATH: `~/.aspire/bin`)
- Architecture tests (fast, run after any cross-module change):
  `dotnet test tests/Premise.ArchitectureTests`
- Tenant-isolation golden suite (needs Docker; Testcontainers Postgres):
  `dotnet test tests/Premise.IntegrationTests`
- Local dev: `aspire run` from `src/Premise.AppHost` (Postgres + api + worker + dashboard)
- Migrations: `dotnet ef migrations add <Name> --project src/Modules/<Module> --startup-project src/Modules/<Module>` (see new-migration skill)
- Format: `dotnet csharpier format .`
- Frontend: `pnpm install && pnpm dev` (workspace arrives in later steps)

## For forks

`.claude/` and this file are **product surface**: forks inherit these guardrails.
The init script (ADR 36) rewrites names here too. Keep this file under ~150 lines;
when an AI session makes a mistake a test could have caught, write the test — only
add a line here when no test could catch it.
