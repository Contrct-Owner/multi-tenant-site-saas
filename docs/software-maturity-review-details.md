# Software maturity review and forward roadmap

## Scope and goal

- **Area:** Whole-project software design, architecture, implementation quality,
  efficiency, test strategy, and production maturity
- **Status:** Active
- **Owner:** Project maintainers
- **Last updated:** 2026-09-03

This review evaluates Premise as a forkable foundation for location/site-based,
multi-tenant SaaS. Ratings are relative to a production-ready template, not to a
prototype or example application. Its purpose is to record what is strong today,
what remains risky, and the acceptance criteria for the next stages of work.

## Executive assessment

Premise is an unusually strong backend foundation. Its tenant isolation,
authorization model, persistence conventions, messaging, audit trail, module
seams, and architectural tests are more deliberate than those in many deployed
applications. The repository is increasingly good at preserving these decisions
when downstream products fork and extend it.

The complete package is not yet a turnkey production platform. The largest gaps
are concentrated at the transition from sound code to an operable product:
production storage and scanning, horizontally safe scheduled work, ordered event
projections, an executable container release, a genuinely typed frontend data
interface, and end-to-end UI verification.

- **Forkable engineering foundation:** 8/10
- **Turnkey production SaaS platform:** 5.5/10
- **Maturity:** Backend foundation approaching late beta; complete product early
  beta

| Area | Rating | Current assessment |
| --- | ---: | --- |
| System and domain design | 8.5/10 | Explicit invariants and thoughtful tenancy, time, lifecycle, and integration models |
| Backend architecture | 8.5/10 | Strong modular monolith with deep shared modules and real adapter seams |
| Tenant and data isolation | 9/10 | EF filters, forced PostgreSQL RLS, role separation, and realistic integration coverage |
| Code cleanliness | 6.5/10 | Disciplined overall, with large composition/page hotspots and some review-history commentary |
| Runtime efficiency | 7/10 at current scale | Good measured behavior around 1,000 sites, with documented full-fleet and sweep ceilings |
| Frontend architecture | 5.5/10 | Sound libraries and state choices, but flat feature structure and a weakly typed network seam |
| Test strategy | 8.5/10 | Excellent database-backed philosophy and broad backend behavior coverage |
| Coverage confidence | 6/10 | No collected line/branch metric and little component/browser coverage |
| Production operability | 5/10 | Release artifact, worker coordination, health, and production storage remain incomplete |

## Changes incorporated since the 2026-09-02 review

Six subsequent commits added cross-tenant writer semantics, migration-helper
compatibility, catalog-owned platform-global declarations, out-of-order action
guidance, and projection serialization.

### Aggregate projection locking

`AggregateLock` is a deep module: two small interfaces hide PostgreSQL advisory
lock mechanics, composite recipient keys, transaction lifetime, and the refusal
to run without an active transaction. Its integration tests cover contention,
independent keys, org-specific copies, misuse outside a transaction, and the
real organization-directory handler.

The lock fixes concurrent duplicate inserts, but serialization does not establish
event order. `OrganizationUpserted` carries no source version or source timestamp.
An older event processed after a newer one can still overwrite the projection.
The next step is a monotonic source version or changed-at value that projection
handlers persist and compare before applying an event.

Key files:

- [`../src/Premise.Platform/Data/AggregateLock.cs`](../src/Premise.Platform/Data/AggregateLock.cs)
- [`../src/Modules/Premise.Modules.Identity/Users/OrgDirectorySync.cs`](../src/Modules/Premise.Modules.Identity/Users/OrgDirectorySync.cs)
- [`../src/Premise.Contracts/Organizations.cs`](../src/Premise.Contracts/Organizations.cs)
- [`../tests/Premise.IntegrationTests/AggregateLockTests.cs`](../tests/Premise.IntegrationTests/AggregateLockTests.cs)

### RLS coverage and platform-global declarations

RLS coverage now discovers every `IOrgScoped` entity from every catalogued EF
model and reads its actual owning-org column. It also includes raw-SQL tables
using the conventional `org_id` column. Deliberate pre-tenant exceptions now live
beside their module declaration in `ModuleCatalog`, with a reason, instead of in
an integration-test allowlist. This improves security-decision locality and
closes the custom-column blind spot.

Key files:

- [`../src/Premise.Api/ModuleCatalog.cs`](../src/Premise.Api/ModuleCatalog.cs)
- [`../src/Premise.Platform/Modules/ModuleDescriptor.cs`](../src/Premise.Platform/Modules/ModuleDescriptor.cs)
- [`../tests/Premise.IntegrationTests/RlsCoverageTests.cs`](../tests/Premise.IntegrationTests/RlsCoverageTests.cs)

### Frozen migration helpers

Removed migration helpers were restored because downstream applied migrations
are immutable source and must continue to compile. `[FrozenAt]` prevents new
migrations from adopting the retired shapes while retaining old signatures and
SQL.

The current architecture guard verifies method names but not complete signatures
or generated SQL. A parameter/overload change or SQL edit could therefore break a
fork while the template test remained green. Add a reflection signature snapshot
and SQL golden tests, or compile representative historical fork migrations in a
compatibility project.

Key files:

- [`../src/Premise.Platform/Data/FrozenMigrationHelpers.cs`](../src/Premise.Platform/Data/FrozenMigrationHelpers.cs)
- [`../src/Premise.Platform/Data/FrozenAtAttribute.cs`](../src/Premise.Platform/Data/FrozenAtAttribute.cs)
- [`../tests/Premise.ArchitectureTests/MigrationHelperTests.cs`](../tests/Premise.ArchitectureTests/MigrationHelperTests.cs)

### Actor attribution

`ActorRef` usefully concentrates the mapping from a person or API key to an
org-bearing audit actor. `ActorGate` is presently an unused, one-line adapter. It
does not yet pass the deletion test: removing it would reproduce one line at
future callers. When real template endpoints need the behavior, prefer deepening
the existing `Gate` module rather than preserving a speculative Contracts-level
seam.

Key files:

- [`../src/Premise.Platform/Kernel/ActorRef.cs`](../src/Premise.Platform/Kernel/ActorRef.cs)
- [`../src/Premise.Platform/Kernel/ActorGateOutcome.cs`](../src/Premise.Platform/Kernel/ActorGateOutcome.cs)
- [`../src/Premise.Contracts/ActorGate.cs`](../src/Premise.Contracts/ActorGate.cs)

## Architectural strengths

- Modules own their schemas, DbContexts, migrations, and vertical behavior.
- Cross-module behavior travels through contracts, read models, and messages
  rather than direct module references.
- `ModuleCatalog`, `ModuleDbContext`, and module persistence registration provide
  high leverage and strong change locality.
- Tenant isolation is enforced twice: named EF query filters and forced PostgreSQL
  RLS under an unprivileged application role.
- Authentication, billing, notifications, object storage, secrets, region
  selection, and authorization are represented by adapter seams rather than
  embedded vendor logic.
- Time, hierarchy, lifecycle, audit, idempotency, and one-owner-per-row rules are
  explicit and supported by ADRs and automated checks.
- Nullable analysis, warnings-as-errors, central package versions, OpenAPI drift
  checks, migration round trips, and realistic Testcontainers coverage make the
  repository difficult to change accidentally.

## Current risks and gaps

### Production storage and scanning — release blocker

`Program.cs` unconditionally registers `LocalObjectStore` and `EicarScanner`.
Unlike local authentication, billing, email, and key wrapping, these adapters are
not rejected in Production.

The local store keeps upload/download tickets in process memory and file bytes on
local disk. In a load-balanced deployment, a ticket created by one API replica
can fail when redeemed on another. The development scanner reads at most the
first 128 KiB, checks only the EICAR string, and treats every other file as clean.

Required outcome:

- Select object storage and malware scanning through configuration.
- Provide real production adapters or clearly defined ports for them.
- Refuse to boot in Production with local storage or `EicarScanner`.
- Add boot-guard and multi-replica ticket-flow tests.

### Multi-replica scheduled work — release blocker

Production documentation permits multiple worker replicas, but every replica
starts the same `PeriodicTimer` hosted services. There is no leader election,
database lease, or stable period-derived deduplication identity at the scheduling
seam. Duplicate compaction, retention, occurrence, connector, closure, and trash
sweeps can therefore be published.

Required outcome:

- Use a distributed scheduler, PostgreSQL advisory lease, or durable per-period
  schedule identity.
- Prove that two worker processes produce one logical sweep per period.
- Make every handler safely retryable even after scheduler deduplication.

### Projection ordering — release blocker for event-fed authority

Aggregate locking stops two handlers from mutating a projection concurrently; it
does not reject a stale event. Projection events need an owner-issued monotonic
version or comparable source timestamp, and each read-model row needs to remember
the last applied value.

Required outcome:

- Add source versioning to `OrganizationUpserted` and future replicated events.
- Ignore duplicates and older versions inside the same locked transaction.
- Test in-order, reversed, duplicate, and concurrent delivery through production
  handlers.

### Health, role validation, and release packaging

`/healthz` is mapped only for the API role even though production documentation
recommends it as the readiness probe for the deployed process. Unknown `ROLE`
values can start an effectively empty host. The repository describes one OCI
image but has no checked-in Dockerfile, SDK container target, Compose reference,
or CI image build.

Required outcome:

- Reject unknown roles during configuration.
- Expose role-appropriate liveness and readiness, including the worker.
- Distinguish process liveness from dependency readiness.
- Build one non-root OCI image in CI.
- Smoke the `migrate`, `api`, and `worker` roles from that image.
- Pre-generate and statically load Wolverine handler code in the production
  artifact.

### Frontend architecture and contract

The console uses good foundations—React Query, centralized session state,
TanStack Router, shared UI primitives, and local UI state—but its feature
boundaries are weak. Large page files mix request types, network calls, mutation
coordination, forms, and presentation. The generated OpenAPI `paths` type is
exported, but the request client still accepts arbitrary path strings, unknown
bodies, and caller-selected response types.

Required outcome:

- Move one vertical at a time into `features/<feature>` modules, beginning with
  sites, roles, and operator workflows.
- Keep routes/pages thin and put network calls in feature data modules.
- Derive method, path, request, and response types from OpenAPI operations.
- Validate untrusted responses and form payloads with schemas.
- Centralize actionable error mapping.
- Replace deprecated TanStack Start `inputValidator()` calls.
- Add route-level code splitting; the console main chunk is currently 504 KiB
  minified.

### Frontend testing and accessibility

Current frontend tests cover session and utility logic only. There are no
component, browser, visual-regression, or automated accessibility checks in CI.

Required outcome:

- Add component and browser coverage for sign-in, org creation/switching, route
  guards, site editing, role assignment, file upload/ingest, and error/conflict
  states.
- Test success, validation, permission, conflict, and network-failure branches.
- Run automated accessibility checks and keyboard-focused flows in CI.

### Architectural guardrail gaps

- `DataConventionTests` scans only Tenancy and Platform rather than all catalogued
  modules.
- The integration dependency rule scans only the WorkOS assembly rather than all
  integration adapters.
- Frozen migration compatibility does not yet snapshot complete signatures and
  SQL.
- `FanOutOrderingTests` is useful executable documentation, but it exercises a
  private example state machine rather than production code.
- Coverlet is referenced, but CI does not collect or report line/branch coverage.

Required outcome:

- Derive module and integration assembly sets from authoritative catalogs or the
  solution graph.
- Add migration-helper compatibility snapshots.
- Report coverage by test tier and module before choosing any enforcement floor.
- Keep behavioral tests at production module interfaces; label worked examples as
  documentation rather than product coverage.

### Efficiency ceilings

The local performance baseline is respectable at approximately 1,000 sites. The
known ceilings are acceptable for the current target but should remain explicit:

- Public site listing retrieves the full fleet and sorts distance in memory.
- Listings feed loads all sites and schedules and filters schedules per site.
- CSV ingest reads and materializes the entire file.
- Site listing performs two counts plus a page query and uses offset pagination.
- Global audit partition upkeep runs once per org retention message rather than
  once per sweep.

Required outcome before claiming materially larger scale:

- Define supported site, tenant, import, and request-volume envelopes.
- Add bounded public reads and ingest limits/streaming.
- Remove repeated full-fleet filtering where benchmarks justify it.
- Separate global partition upkeep from per-org retention.
- Add repeatable multi-tenant worker and high-cardinality fleet baselines.

### Code cleanliness

The code is generally disciplined, but several files have become maintenance
hotspots. `Program.cs` combines role selection, adapters, auth, telemetry, rate
limiting, messaging, middleware, development endpoints, and host startup. Several
console pages are 300–480 lines, and some comments preserve review history rather
than only durable constraints.

Required outcome:

- Split the composition root by substantial concern—not into one-line wrappers.
- Refactor frontend hotspots through feature work, not a cosmetic file shuffle.
- Move historical rationale to ADRs/review documents and leave invariant-focused
  comments beside code.
- Keep README counts and maturity claims synchronized with the repository.

## Prioritized roadmap

### Phase 1 — production correctness and safe deployment

- [ ] Production-selectable storage and scanner with fail-closed boot guards
- [ ] Distributed ownership of recurring worker schedules
- [ ] Monotonic versions on event-fed projections
- [ ] Validated process roles and worker health/readiness
- [ ] CI-built non-root OCI image and three-role smoke test

**Exit criterion:** Two API replicas and two worker replicas can run from the same
artifact without local state assumptions, duplicate logical schedules, stale
projection regression, or missing health probes.

### Phase 2 — frontend contract and critical-flow confidence

- [ ] OpenAPI-constrained request client
- [ ] Sites, roles, and operator feature modules
- [ ] Schema-based validation and consistent errors
- [ ] Browser tests for the highest-value tenant and operator workflows
- [ ] Automated accessibility checks
- [ ] TanStack deprecation cleanup and console route splitting

**Exit criterion:** Changing an endpoint contract or breaking a critical UI flow
fails CI with a specific, behavioral error.

### Phase 3 — guardrail completeness and measurable quality

- [ ] Complete module/integration assembly discovery
- [ ] Migration helper signature and SQL compatibility snapshots
- [ ] Coverage reporting by module and test tier
- [ ] Production-interface tests for ordering/idempotency recipes
- [ ] Remove or absorb speculative shallow modules such as unused `ActorGate`

**Exit criterion:** Every architectural guarantee claimed in primary documentation
has an automated check over the complete relevant code set.

### Phase 4 — scale and maintainability

- [ ] Publish supported operating envelopes
- [ ] Bound or stream full-fleet and ingest paths
- [ ] Benchmark multi-tenant workers and larger fleets
- [ ] Separate global and per-tenant maintenance work
- [ ] Reduce backend and frontend maintenance hotspots as related features change

**Exit criterion:** Performance and operability claims are backed by repeatable
benchmarks at published data and replica counts.

## Verification snapshot

Verified against `7582d09` on 2026-09-03:

- `dotnet build Premise.slnx -c Release -m:1 /nodeReuse:false` — passed, zero
  warnings and zero errors
- Architecture tests — 35 passed
- Platform unit tests — 34 passed
- Integration Release shard 1 — 95 passed
- Integration Release shard 2 — 92 passed
- Frontend typecheck — passed
- Frontend lint — passed
- Frontend tests — 12 passed
- Console and public production builds — passed

Remaining build warnings:

- Console main JavaScript chunk is 504.01 KiB minified / 149.15 KiB gzip.
- Three public routes use deprecated TanStack Start `inputValidator()`.

The integration-shard stall observed during the earlier review did not reproduce
and is not considered an active defect.

## Parent and related links

- Parent summary: [`../README.md`](../README.md)
- Architecture review: [`architecture-review-2026-09-02.md`](architecture-review-2026-09-02.md)
- Architecture decisions: [`decisions/README.md`](decisions/README.md)
- Production topology: [`production.md`](production.md)
- Operational runbook: [`runbook.md`](runbook.md)
- Performance baseline: [`perf-baseline.md`](perf-baseline.md)
- Cross-tenant sharing: [`cross-tenant-sharing.md`](cross-tenant-sharing.md)

