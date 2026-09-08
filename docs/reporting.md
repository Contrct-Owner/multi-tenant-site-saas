# Reporting for implementing applications

- **Status:** Shared site-file workflow, subscription backfill and local Linux production-image execution verified. Live-provider and target-deployment qualification remain separate. See the latest [deployment and recovery evidence](#deployment-and-recovery-follow-through--2026-09-07).
- **Owner:** Project maintainers.
- **Updated:** 2026-09-07.
- **Parent:** [Premise README](../README.md).

## Revised site-library workflow — supersedes earlier artifact-only decisions

The user has changed the product workflow. Start generation by selecting one or
more sites in the site library, then choosing Generate report. A site detail page
can invoke the same action for that site. Capture the selected IDs when opening
the options dialog; do not silently expand the selection from a filter.

The Reports page is a run index. Each run has a permanent URL and visible run ID,
selected sites, status, results, failures, retries and batch downloads. Generation
navigates to `/reports/<runId>`; links from site files must return to that run.
The site library is the primary generation entry point, not a second site picker
on the Reports page.

**Storage change implemented and functionally verified:** generated PDFs become durable
files associated with their sites. Individual PDFs attach to their respective
site. An aggregate is one file linked to every included site, with access requiring
the full included selection; do not duplicate its bytes or grant cross-site access
merely because it appears under one site. ZIP bundles belong to the run. Files
link back to their originating run, and runs link to their files and sites.
Published PDFs follow the normal file lifecycle, including trash, legal hold and
organization purge, rather than the earlier seven-day report-artifact expiry.
Keep run provenance while files still reference it. Temporary artifacts, failed
attempts and ZIP bundles can retain bounded cleanup windows.

**Access decision accepted:** generated files are available to authorized site
users under the same `files:read` and `sites:read` scopes, with current access to
all included resources. They are no longer requester-only. File management uses
`files:manage` over the included sites; read-only users do not need report-generation
permission or an enabled generation entitlement. New generation retains the
existing report permission and quota gates. Retry/cancel still belong to the
requester. Aggregate files and run-wide ZIPs require the full included selection.

The file library now records site IDs and origin metadata; shared access, durable
publication, lifecycle and browser evidence are recorded in the latest checkpoint. The historical
verification below applies to the earlier private-artifact model unless explicitly
requalified after this change. Entitlement reservations and charging remain
required, with one charge per generated PDF, not per site association.

## Purpose and current state

Premise owns reliable report execution and delivery. Forks own their report
content, layouts, branding, options, data selection and report-specific permissions.
The template must ship working examples, not only interfaces.

Typed report-job APIs, production reference definitions and a renderer proof now
exist, along with the report console, as described in the evidence ledger below.
Local Linux production-image qualification now includes actual reference jobs and
S3-backed downloads; external basemap and target-deployment checks remain pending.
Existing organization and audit exports generate JSON/JSONL ZIPs
in memory. They establish useful storage and messaging patterns, but do not prove
bounded bulk PDF generation. No reporting performance or production acceptance
is claimed by this design document.

Existing foundations:

- [Organization exports](../src/Modules/Premise.Modules.Storage/Offboarding.cs)
  and [audit exports](../src/Modules/Premise.Modules.Storage/ExportAuditTrailHandler.cs).
- [File download authorization](../src/Modules/Premise.Modules.Storage/Endpoints.cs)
  and [file lifecycle](../src/Modules/Premise.Modules.Storage/Data/FileObject.cs).
- [Configured raster basemaps](../src/Modules/Premise.Modules.Tenancy/Organizations/MapBasemapsEndpoints.cs).
- Wolverine durable messaging, tenant envelopes, transactional outbox, shared
  HTTP idempotency and database-backed maintenance coordination.

## Agreed product contract

The decisions below remain the product target. Implementation and verification
checkpoints below record which requirements have been demonstrated; older
checkpoints describe the state at that time, not the current completion status.

| Concern | Decision |
| --- | --- |
| Individual report | One PDF for one site. |
| Aggregate report | One PDF spanning an explicitly captured collection of sites, including organization-wide selections. |
| Bulk report | One registered report type and shared options applied to many sites, producing separate PDFs and a ZIP. This is distinct from an aggregate PDF. |
| Initial limits | At most 100 individual reports per bulk request; separately, at most 100 sites per aggregate report. Fork-configurable. Reject oversized requests before enqueueing; never truncate silently. |
| Data timing | Capture selected site IDs and options at submission. Read current data during each generation and record generation time. No point-in-time snapshot guarantee, including within one report. |
| Organization scope | Organization-wide requires access to every included site. A narrower request must be explicitly labeled “accessible sites.” Capture that selection at submission. |
| Authoring | Code-defined report types. Users select a registered report and its supported options; no user-facing report designer. |
| Generation permission | Add `reports.generate`, granted to organization managers by default, plus every applicable site and resource permission. Forks may grant it to other roles. |
| Download ownership | Authorized site users with file-read and site-read access to every included resource. Generation permission is separate; retry/cancel remain requester-owned. |
| Partial failures | Keep successful PDFs. Finish bulk jobs as completed with errors and include a failure manifest in the ZIP. If all items fail, provide an error summary without an empty ZIP. |
| Retry | Retry failed items using fresh data; retain successful PDFs and their original generation times. Rebuild the ZIP and manifest. Generate again creates a new batch for a fully fresh result. |
| Cancellation | Stop scheduling remaining items and cooperatively cancel active generation. Retain completed PDFs; mark the job canceled; skip automatic ZIP creation. |
| Downloads | Successful PDFs are persistent site files. ZIP downloads require current access to every included site/resource and every included PDF to remain Clean. |
| Retention | Published PDFs follow file trash/hold/erasure. ZIPs and unpublished attempts expire after seven days; metadata without published PDFs after 30 days. Preserve provenance for published files, including tombstones, until organization purge. |
| Duplicate submissions | Reuse the existing idempotency contract: same key and request returns the original job; explicit regeneration uses a new key. |
| Console | Select sites in the library, then generate. Reports indexes runs with stable URLs, progress, failures, retries and ZIPs. PDFs appear in site Files and the org Files library, with links back to the run. |
| Language | English layouts initially, with bundled fonts covering Latin-script names and addresses. Fork-replaceable fonts and labels. Complex-script and right-to-left layout are not initially qualified. |

### Commercial entitlements and report accounting

Required for completion. Backend accounting and console allowance controls are implemented;
full lifecycle and deployment qualification remain in progress.
`reports.enabled` controls new submissions and explicit retries, separately from
`reports:generate` permission. Its template default is true; existing entitlement
assignments and expiring exceptions can override it. Disabling new generation
must not prevent cancellation or authorized downloads of previously accepted work.

The accounting unit is a PDF output: single and aggregate requests reserve one
unit; bulk requests reserve one per captured item. ZIP assembly, downloads, and
redelivery do not add usage. The quota period is a UTC calendar month. Reserve
all requested units atomically with the durable job, or reject the whole request.
On successful publication convert the item's reservation to consumed usage in
its reserved month. Failure or cancellation releases unfinished units. Explicit
retry reserves only failed items against the retry month's quota; successful
items retain their bytes, generation timestamps, and original charge. A new
regeneration is a new reservation. Pending reservations survive month rollover
and process crashes; artifact/metadata deletion must not erase quota accounting.

`reports.monthly` is a strict Limit/Block entitlement, defaulting to 1,000 PDF
outputs per UTC month on the free/default tier. The existing paid plan bundles
set Growth to 5,000 and Scale to 50,000 PDFs per month. These are editable template
allowances, not measured capacity or hosting-cost guarantees. Subscription changes
use the existing billing synchronization; cancellation returns to catalog defaults
without refunding usage or revoking already accepted reservations. Forks change
defaults in the entitlement/plan catalogs; operators use the
existing assigned values and expiring exceptions. Its usage probe includes both
reserved and consumed PDFs, so existing downgrade preflight sees outstanding work.
`GET /api/reports/quota` exposes the current allowance to permitted report users.

On a drained deployment, the migrate role also runs `PlanEntitlementBackfill`
after schema migrations. It fills missing bundle codes for known Active,
Trialing and PastDue subscriptions, including `reports.monthly`, using the fork's
current `PlanCatalog`. It pages through 200 subscriptions at a time and inserts
with `ON CONFLICT DO NOTHING`. Existing assignments of every source, their
timestamps, and entitlement exceptions remain untouched. Canceled subscriptions,
unknown statuses and retired/unknown plan IDs receive no new plan values.
An interrupted pass can be rerun safely. This is missing-code backfill, not a
reconciliation of changed prices or existing assigned limits; billing events
remain responsible for subscription changes.

Upgrade order: drain/stop API and worker writers, run the new image's migrate
role against each deployed regional database using its owner credentials, check
the successful exit and inserted-row count, then start API and worker with the
application credentials. Do not run this backfill concurrently with older billing
writers. Operator exceptions continue to override the new underlying plan value
until they expire. No manual per-organization webhook replay is needed.

`reporting.quota_entries` stores one entry per PDF item, with its reserved month
and consumed state. Admission uses the existing capacity advisory-lock helper and
the same Reporting transaction as the job/outbox. Settlement and cancellation
commit with job state changes. Entries have forced tenant RLS and deliberately
have no cascading job foreign key: deleting report metadata does not refund usage.
Consumed entries remain until organization purge; they are accounting records,
not retained PDFs. Jobs accepted before the quota migration are grandfathered;
explicit retries after the migration reserve only their failed items. A ZIP-only
retry needs no additional quota even if a later plan change lowers the ceiling.

Use the existing entitlement values and transactional capacity infrastructure.
The approximate append-and-rollup meter alone is insufficient for strict
admission. Expose enabled status, ceiling, consumed and reserved counts, remaining
capacity and period in the console. Verify concurrent replicas, quota boundaries,
month rollover, retry/cancel/crash behavior, and tenant isolation. Operational
queue, batch-size, and concurrency limits remain separate from commercial quotas.

### Reference reports

The individual site report includes identity/address, hierarchy path, status,
custom attributes, operating hours, a location map with selected overlays, and
selected photographs. Use existing server-authoritative schedule semantics and
site time zones. The aggregate reference includes a summary table and overview
map. Checklist results and audit history belong to additional report definitions.

Photographs come from authorized application storage. Map sources are explicitly
configured providers; forks may register additional trusted sources in code.
Requests do not supply arbitrary image URLs.

Maps support a configured basemap, selected-site markers and explicitly selected,
authorized application overlays. Capture map choices at submission and use a
defined report extent/zoom, not the user's current browser viewport. Recheck
overlay access at generation and download.

Report definitions mark visuals required or optional. Missing required visuals
fail that report. Optional failures produce a labeled placeholder and a warning
in both the PDF and batch manifest. Reference visuals are optional. Permission
revocation is an authorization failure, not an optional-image fallback.

### Permission changes

Track the protected sites, photographs and overlays actually included in each
artifact. Requester ownership alone is insufficient: download requires current
access to every included protected resource. A ZIP requires access to all
resources included in its PDFs and manifest. Losing access blocks the whole
artifact; individually authorized PDFs remain available.

Recheck access before generation. An aggregate report fails if any selected site
becomes unauthorized; never silently omit it. In a bulk job, fail the affected
individual items. Job details and failure messages must not disclose protected
names or data after access is lost; use safe summaries and identifiers as appropriate.

The existing storage contract checks authorization when signing a short-lived
download URL. Already issued URLs remain usable until their expiry, and already
downloaded copies cannot be revoked. This design does not promise instantaneous
revocation of issued URLs. URL lifetime must never extend beyond artifact expiry.

## Proposed implementation architecture

These are implementation recommendations, not claims of existing functionality.

### Ownership and extension point

Use a Reporting module with its own schema, DbContext, migrations and tenant RLS
for jobs, items and artifact/dependency metadata. Reuse existing messaging and
object-store ports. Follow [module ownership](decisions/0017-dbcontext-schema-per-module.md)
and [Wolverine](decisions/0023-wolverine.md); do not introduce a second job runtime
or cross-module EF access.

Start with one registered report-definition contract carrying a stable type ID,
version, supported scope/options, authorization/data-selection behavior and PDF
generation. Keep the precise C# signature contingent on the working reference
slice. A fork should add a report through registration without modifying job,
ZIP, retention or download handlers. Use existing cross-module read contracts,
adding only narrowly required contracts for missing data access.

The default renderer uses an established C# library. PDFsharp + MigraDoc is
integrated into the production reference definitions and locally verified. Their MIT
license requires retaining the notice; the Core build supports .NET operating
systems and requires explicitly supplied fonts. Verify the selected package and
font licenses at implementation time. Sources:
[license](https://docs.pdfsharp.net/General/License/License.html),
[platform and font requirements](https://docs.pdfsharp.net/General/Overview/FAQ.html).

Use the library's layout and PDF APIs directly. Do not build a PDF engine, generic
document language, renderer factory hierarchy or visual designer. The extension
point produces a PDF artifact so forks can substitute their rendering approach.

### Durable execution

1. Validate options and limits; resolve and authorize the explicit site/resource
   selection. Persist the job/items and enqueue durable work atomically in the
   Reporting transaction using the existing outbox.
2. Workers load current requester permissions and source data under the correct
   organization/region. No saved authorization boolean substitutes for current access.
3. Generate each PDF with bounded concurrency and resource budgets. Perform slow
   image fetching/rendering outside long-running database transactions.
4. Write private artifacts, then durably record completion and dependencies.
   Object storage and PostgreSQL are not one atomic transaction: stable artifact
   identities, idempotent transitions and orphan cleanup must handle crash windows.
5. Once items reach terminal states, assemble the ZIP and manifest from successful
   PDFs. Persist bundle readiness only after storage succeeds. A bundle failure
   must be recoverable without regenerating successful PDFs.
6. Authorize each download and issue an expiring URL. Cleanup removes expired
   artifacts and later job metadata, including dependencies and sensitive inputs.

Persist enough state to distinguish queued/running/completed/completed-with-errors,
failed and canceled jobs; item states distinguish success, failure and cancellation.
Expose artifact readiness separately: successful item generation does not imply
the ZIP is ready. Use guarded transitions so duplicate delivery, retries, worker
death and cancellation cannot publish stale artifacts or count an item twice.

Cancellation is cooperative. It does not guarantee an immediate stop inside a
synchronous PDF-library call. Bound those calls, check cancellation between stages
and prevent a canceled attempt from publishing a later ZIP.

Pin the report-definition version with the job. If a deployment cannot execute
that version, fail clearly rather than silently substituting a new definition.
Use the existing HTTP idempotency lifetime and conflict behavior, not an indefinite
deduplication promise. See [ADR 29](decisions/0029-idempotency-key-all-unsafe.md).

### Protected artifacts and lifecycle

Do not register report artifacts as ordinary broadly downloadable `Clean` files
without enforcing reporting's requester and dependency checks. The existing
generic Files endpoint checks `files.read`; it does not enforce this report
contract. Prefer private report-owned artifact metadata and the existing object
store port. Any shared Files integration must preserve the stricter checks on
every download/preview path.

Treat jobs and generated artifacts as regenerable ephemera with explicit hard
deletion/expiration; all timestamps are UTC instants. Record report lifecycle
events through existing auditing, without logging document content, credentials
or signed URLs. Org offboarding must remove report artifacts and stop queued work.
Source-file legal holds must not accidentally be overridden by report cleanup.

Proposed retention detail: terminal completion (including failure/cancellation)
starts retention. Failed-item retry does not silently extend existing successful
artifacts' expiry; refuse retries that can no longer form a complete retained
batch and offer Generate again. Confirm these boundary semantics in the API
contract before coding them; the interview agreed the durations, not these edges.

### Maps, photographs and resource limits

Configured browser tile URLs are not automatically trusted for worker-side fetches.
Add explicit server-side provider allowlisting and safe redirect/address handling,
bounded network timeouts and response sizes. Never leak provider keys into logs,
PDFs or manifests. Preserve map attribution and verify provider permission for
static export/caching; do not assume browser use authorizes bulk report rendering.

The PDF library does not itself solve application map rendering. Prove the map
path with a maintained rendering dependency or provider capability, including
application overlays. Select it after inspecting the existing spatial formats.
Support deterministic local fixtures so tests require no paid provider.

Only approved clean images are usable. Bound compressed bytes, decoded pixels,
image count, PDF size, aggregate output, temporary disk use and generation time.
Downsample photographs appropriately. The current file model has no site-photo
association; reference photo selection needs an explicit authorized input rather
than an invented existing relationship.

Avoid accumulating all PDFs and a complete ZIP in process memory. Use the existing
object-store stream interface and bounded temporary files where required by the
PDF/ZIP libraries. Prove adapter behavior instead of assuming streaming support.
Apply per-worker concurrency limits and shared admission/fairness as needed so
adding replicas cannot bypass a promised fleet-wide limit. Numeric execution,
queue, image and byte budgets remain to be established by qualification; the
agreed 100-item limits alone do not bound resource use.

## Implementation sequence and acceptance

1. **Renderer and visual proof.** Produce real reference PDFs using library layout,
   bundled fonts, photographs, basemap, markers and representative overlays in the
   Linux production image. Inspect page rendering, pagination, long text, tables,
   image orientation, attribution and optional placeholders. Measure resources.
2. **Single-report vertical slice.** Add Reporting persistence/RLS, registration,
   capability, idempotent submission, durable generation, protected download,
   lifecycle cleanup and a minimal console path. Demonstrate a second registered
   aggregate report without changes to execution plumbing.
3. **Bulk and recovery.** Add bounded fan-out, progress, cancellation, failed-item
   retry, individual downloads and ZIP/manifest assembly. Exercise crash windows
   and multiple workers. Preserve successful artifacts on retry.
4. **Complete console and fork guide.** Add all agreed controls, useful validation,
   warnings and expiration display. Document how a fork registers a report,
   supplies authorized data/visuals, changes options/layout/fonts and sets limits.
5. **Qualification and deployment documentation.** Run behavioral, architecture,
   browser and Linux-image checks. Record supported resource ceilings and required
   map/font configuration. Test cleanup and failure recovery before claiming ready.

Required acceptance scenarios:

- One site PDF, one aggregate PDF and a 100-item bulk ZIP; over-limit requests
  rejected before work exists. Validate archive entries and actual PDF pages.
- Cross-org isolation, authorized site-reader access, revoked site/photo/overlay access
  before generation and signing, and no bypass via generic Files endpoints.
- Explicit full-organization versus accessible-site selection; no silent truncation.
- Mixed success with accurate manifest; all-failed without empty ZIP; optional
  visual warning versus required visual failure.
- Worker killed during generation, storage completion or ZIP assembly; duplicate
  deliveries, submission retries and cancel/retry races; bounded resource use.
- Cancellation retains completed PDFs and prevents later ZIP publication.
- Failed-item retry uses fresh data while successful artifacts retain timestamps.
- Seven-day artifact and 30-day metadata cleanup, org purge, orphan/temp-file
  cleanup and download TTL bounded by remaining retention.
- Malformed/oversized images and unavailable map providers produce bounded failures;
  protected resources and secrets are absent from unsafe logs/error summaries.
- Console accessibility and recovery states; usable output from a second fork-style
  report definition without changes to generic execution handlers.

## Remaining technical checks

No further broad product interview is required before starting the proof. Resolve
these by inspecting code and running experiments; surface a decision only if a
finding changes agreed behavior:

- Exact renderer package/version and font distribution, map rendering strategy,
  supported overlay formats and provider export rights.
- Current permission resolution from background work, including suspended/removed
  requesters and permissions for resources represented inside an overlay.
- Option validation and console extension shape without inventing a form framework.
- Concrete timeout, concurrency, queued-job, image, byte and temporary-disk limits.
- Retention/retry boundary semantics, artifact storage cleanup ownership and
  cancellation behavior of the selected rendering path.

## Implementation evidence (2026-09-07)

The [standalone PDF proof](../tools/Premise.Tools.ReportingProof/README.md) pins
PDFsharp/MigraDoc 6.2.4 and supplies licensed Liberation fonts explicitly. It
generates a three-page site fixture and a five-page, 100-row aggregate fixture,
then bundles both PDFs and a warning manifest using file-backed ZIP output.
PNG map and JPEG building fixtures are synthetic pre-rendered illustrations.
They prove image embedding, not production map composition or photograph handling.

`bash tools/reporting-proof.sh premise:compat-dev /tmp/premise-reporting-proof`
passed in the existing Linux x64 production runtime as a non-root user with no
network, read-only root filesystem, one CPU and a 256 MiB memory limit. The local
run took 1,617 ms and reported 130,027,520 bytes peak process working set. Its ZIP
was 96,017 bytes. This is one small fixture run under x64 emulation on an ARM host,
not a production capacity benchmark or a 100-PDF bulk-generation qualification.

All eight pages were rendered and visually inspected. The runnable `verify.py`
check passed embedded-font, Latin diacritic, image, page-number, all-100-row,
all-24-observation, exact ZIP-entry-byte and manifest checks. Build and formatting
checks passed. The first Linux run failed because MigraDoc required its predefined
error font; explicitly configuring error and bullet fonts resolved it without
relying on host fonts. The test container was removed after the run.

The renderer is isolated in a tool project; the API does not reference the new
package. At this proof checkpoint, actual map/overlay composition, safe image
handling, durable execution, protected downloads and the console were still
unimplemented. Subsequent backend progress is recorded below. No cloud resources were created.

### Reporting foundation

The generated Reporting module is registered in the solution, API, Wolverine
discovery and module catalog. Its initial migration creates tenant-isolated jobs,
items and private artifact metadata with forced RLS and organization-qualified
parent keys. Artifact intents can be recorded before object writes, preserving
cleanup evidence across crashes. At this foundation checkpoint no submission or
download endpoint exposed it yet; the workflow checkpoint below supersedes that gap.

Tenancy and Spatial now implement explicit org/scope report read contracts for
site details, readable hierarchy paths, materialized operating windows and
polygons/multipolygons including holes. The overlay source enforces a 20,000-vertex
budget inside a short repeatable-read transaction before materializing geometry.
The first real-overlay test caught EF's invalid `ST_NumPoints(geography)` query;
the explicit `ST_NPoints(geom::geometry)` check passes. Stored data remains WGS84
geography. These read adapters are not yet a map renderer.

Identity supplies a current-member lookup for worker authorization, denying absent
memberships and non-active organizations and never restoring impersonation. The
capability is spelled `reports:generate` in the existing colon-separated catalog
(the interview's `reports.generate` is its conceptual name). New Admin presets
include it; Owner already grants it through its wildcard. Existing custom roles
are not silently rewritten. The generated capability catalog and role editor
include the permission.

Verification: **15 integration checks** passed for foundation persistence, global
RLS coverage and module migration round trips; **54 architecture checks** passed.
After adding real data and current-requester checks, **6 reporting integration
tests** passed, covering cross-org reads/writes/child keys, cascade deletion,
membership removal/suspension, site selection overflow, readable hierarchy,
operating windows, and scoped multipolygons with holes. Local logs are
`/tmp/premise-reporting-foundation-tests.log`,
`/tmp/premise-reporting-architecture.log` and
`/tmp/premise-reporting-source-final.log`; generated migration SQL was inspected at
`/tmp/premise-reporting-initial.sql`.

Org export contributes safe report lifecycle metadata rather than private report
bytes. An org-purge handler is wired for stored artifacts and jobs; its interaction
with in-flight generation must be completed and tested with the job runner.
Background execution, PDF integration, download authorization, lifecycle recovery,
bulk workflow and the console reporting page remained required at this checkpoint.

### Backend workflow evidence

Reporting now has typed routes for type discovery, submission, job list/detail,
cancellation, failed-item retry and artifact download. `IReportDefinition` is the
registered fork extension point with version, option validation, current-resource
authorization and stream-based rendering. The reference production definitions
are not registered yet; workflow tests register a test definition explicitly.
Its fixture bytes exercise execution/storage, not PDF validity or layout.

The runner uses the existing durable Wolverine queue and short Reporting
transactions. A per-job database lease and revision checks prevent stale attempts
from publishing results. Unique artifact intents precede object writes; rendering
and ZIP assembly use bounded temporary files outside database transactions.
Failed-item retry retains successful PDF bytes, and superseded ZIP revisions are
not downloadable. Cancellation is polled during generation and bundling; the
underlying renderer must honor its token at cooperative boundaries.

A worker-only periodic service uses the existing per-org sweep lease to recover
expired job leases and remove expired bytes before metadata. Org purge first
invalidates publication and retains intents until any writer lease drains. Additional crash-window qualification remains pending. PostgreSQL transports
report messages to worker-only listeners outside the combined Testing host, with
one concurrent report per worker process. Fleet qualification is recorded below.

Current configurable defaults are 100 bulk items, 100 aggregate sites, 10 queued
or running jobs per org, a 60-second per-item/bundle timeout, a 20 MiB PDF ceiling,
and a 200 MiB ZIP ceiling. These are operational bounds, not measured supported
production workloads. The byte wrapper also checks seeks and backpatches.

Verification after these changes:

- **13 combined reporting integration tests passed**: six persistence/read checks
  and seven workflow checks, including current site-scope revocation at download.
- After adding the output-budget case and bundle cancellation polling, **all eight
  workflow tests passed**. They cover requester isolation, generic-Files bypass
  rejection, partial retry preserving successful bytes, active cancellation,
  submission idempotency, oversized selection rejection, expired-lease recovery,
  two-stage retention, all-failed output limits and temporary-file cleanup.
- **54 architecture tests passed** and the running API's OpenAPI snapshot test
  passed. Concrete HTTP service parameters are explicitly `[FromServices]` after
  a test exposed Wolverine treating the cancellation limits service as JSON input.
- Logs: `/tmp/premise-reporting-workflow-final.log`,
  `/tmp/premise-reporting-workflow-budget.log`,
  `/tmp/premise-reporting-workflow-architecture.log`, and
  `/tmp/premise-reporting-openapi.log`.

Still required: real PDF definitions with safe map/photo rendering, resource
dependency authorization tests for visuals, console workflow and fork guide,
broader race/orphan/offboarding coverage, 100-PDF qualification, multi-replica
process-death recovery and final production-image/visual verification. No completion
claim follows from these narrow workflow tests.

### Production rendering helpers — 2026-09-07

The Reporting module now references PDFsharp/MigraDoc 6.2.4 and owns its embedded,
licensed Liberation Sans fonts. The standalone proof uses the production
`ReportFonts` resolver rather than a separate file-based resolver. Font and
library license notices are copied into published output. A fork may install a
PDFsharp font resolver before the first render; the reference family must remain
resolvable unless its layouts are also changed. Font configuration is process-wide.

`ReportImages` uses [SkiaSharp 3.119.4](https://www.nuget.org/packages/SkiaSharp/3.119.4)
and its Linux native-assets package (MIT) for JPEG/PNG decoding, EXIF orientation,
downsampling and metadata-free JPEG output. Input is bounded to 8 MiB, 20 million
pixels and 10,000 pixels on either axis before decoded pixel allocation; output
is at most 1,600 pixels on either axis. Other formats, animated images and
incomplete decodes are rejected. These are helper limits, not yet the full
report-level image-count and memory qualification.

Verification:

- `dotnet test tests/Premise.IntegrationTests --filter FullyQualifiedName~ReportingRenderingTests`
  passed **10 cases**: a real reopened PDF with bundled fonts and a normalized
  image, malformed/oversized input, downsampling, and all eight EXIF orientations.
  Log: `/tmp/premise-reporting-rendering-tests.log`.
- `dotnet test tests/Premise.ArchitectureTests` passed **54 tests**.
  Log: `/tmp/premise-reporting-rendering-architecture.log`.
- `bash tools/reporting-proof.sh premise:compat-dev /tmp/premise-reporting-production-helpers`
  passed in Linux x64 with no network, read-only root, 1 CPU and 256 MiB memory.
  Eight synthetic PDF pages took 2.558 seconds; reported peak working set was
  132,411,392 bytes (~126 MiB). ZIP size was 109,759 bytes. The proof's verifier
  passed embedded-font, image, text, pagination, table-row and archive checks.
  All eight pages were rendered with Poppler and their contact sheet inspected;
  no clipping or missing image/font placeholders were observed.
  Artifacts and `metrics.json`: `/tmp/premise-reporting-production-helpers/`.

This checks production helper code inside the existing Linux runtime image with
newly published proof binaries. It does **not** verify a rebuilt production API
image, real reference definitions, configured map fetching/composition, protected
photo integration, the console, or a 100-PDF campaign. Those remain required.

### Reference definitions and visual integration — 2026-09-07

Registered scoped definitions `site` (single/bulk) and `sites-summary` (aggregate),
version 1, now execute through the durable workflow and return real PDFs. The
individual layout reads current site identity/address, hierarchy, status, custom
attributes and seven days of materialized operating windows in the site time
zone. Absence of materialized windows is labeled explicitly, not interpreted as
closed. The summary lists the captured sites. Both include generation time and
the captured-selection description. Reference text input is limited to 300,000
serialized UTF-8 bytes; an oversized input fails rather than silently truncating.

Reference options use this shape:

```json
{
  "mapProvider": "licensed-provider",
  "mapZoom": 14,
  "overlayIds": [],
  "photos": { "<site UUID>": ["<clean stored-file UUID>"] }
}
```

All fields are optional. Unknown fields and unknown provider IDs are rejected.
`mapZoom` is the maximum zoom (0–18); the 768×512 overview zooms out to fit the
captured sites with padding. It does not use a browser viewport. Selected overlays
are clipped to that site-based extent, use a consistent report style, and retain
polygon/multipolygon holes. The summary does not accept photograph options.
Individual reports accept at most ten photos, normalize them through the bounded
image helper, and cap embedded photograph bytes at 16 MiB. Optional failures add
PDF and manifest warnings. Successful visual IDs become download dependencies;
current access is checked again before signing. Quarantined files are refused.

Configure server-side raster basemaps in trusted deployment configuration:

```json
{
  "Reports": {
    "Basemaps": {
      "licensed-provider": {
        "Url": "https://tiles.example.invalid/{z}/{x}/{y}.png",
        "Attribution": "Replace with the provider's required attribution"
      }
    }
  }
}
```

Supply a provider whose terms permit this server-side PDF use. No provider is
enabled by default. Deployment secrets may supply a provider key in its configured
URL; URLs and keys are never returned by the catalog or placed in PDF warnings.
`GET /api/reports/basemaps` returns only provider IDs and attribution to users
allowed to generate reports. Tenant/browser basemap URLs are not trusted worker
inputs. Connections require HTTPS on port 443, use validated public DNS addresses
without a second resolution, disable proxies/cookies/redirects, and reject local,
private and transition-network destinations. Tile bytes pass the bounded image
decoder. Provider transport and image failures produce the optional map placeholder.

Verification so far:

- `/tmp/premise-reporting-reference-final.log`: **33 reporting and OpenAPI checks
  passed** after adding the basemap catalog. **54 architecture tests passed**
  (`/tmp/premise-reporting-reference-architecture.log`). Client types were
  regenerated from the running API schema.
- `/tmp/premise-reporting-reference-third.log`: **31 tests passed**, covering the
  two real reference definitions through API submission, durable execution,
  storage and download; existing workflow behavior; image/font tests; public
  address filtering; polygon-hole and marker rendering.
- `/tmp/premise-reporting-photograph-second.log`: a real clean stored photograph
  was embedded, recorded in dependencies, then quarantined; the existing report's
  download became forbidden. The PDF's image resource was inspected by the test.
- Representative production PDFs from deterministic database fixtures were
  copied to `/tmp/premise-reporting-reference/site.pdf` and
  `/tmp/premise-reporting-reference/sites-summary.pdf`. Both were rendered with
  Poppler and inspected. These fixtures intentionally have no configured map
  provider and correctly display the optional-map placeholder.
- The tests exposed JasperFx's mixed singleton/scoped enumerable limitations.
  Report definitions are registered with `AddScoped<IReportDefinition, T>()`,
  including the test definition; only its retry-attempt state remains singleton.

Remaining visual verification includes actual configured tile retrieval and full
basemap composition, richer production PDF fixtures, overlay permission revocation,
provider failure cases and Linux production-image qualification. Console, complete
fork guidance, 100-PDF and multi-replica recovery acceptance remain outstanding.

### Console workflow — 2026-09-07

The console's `/reports` feature is now linked in navigation for users with
`reports:generate`. It offers searchable, paged site selection, explicit
accessible-site and organization-wide selections, single/bulk/aggregate modes,
configured basemap and overlay choices, and clean JPEG/PNG photo selection per
explicitly selected site. Summary reports omit photograph choices. Default
reference options use labeled controls; additional fork definitions can receive
their code-defined options as a JSON object until the fork supplies its own form.

The latest 50 requester-owned jobs can be opened for polling progress, per-item
site IDs, generation times, failures and warnings. Downloads use a fresh
authorization request. The page supports cancellation and failed-item retry,
explains that retry retains successful bytes, and disables expired downloads and
retries. A new submission regenerates all items. Report-specific requests are
contained in the feature's API module and routing remains a thin lazy import.

Submissions supply a caller-owned idempotency key through the existing typed
client. Retrying the same form after an uncertain network outcome reuses that
key; changing the payload or completing submission produces a new key. The key
is component-local, not persisted across page reloads. Existing server replay
and tenancy rules remain authoritative.

Verification:

- `vitest run src/features/reports/reports-page.test.tsx src/lib/client.test.ts`
  passed **26 checks**, including eight report UI/validation cases plus shared
  client coverage. Checks include the HTTP idempotency header, uncertain-outcome
  key reuse, permission gating, selection validation, aggregate submission,
  retry, cancellation conflict, download access failure and expiration.
  Log: `/tmp/premise-reporting-console-final.log`.
- The API OpenAPI snapshot passed after adding item site IDs and warning arrays;
  generated client types were refreshed. Console TypeScript checking passed.
- `vite build` passed; the existing large-chunk advisory remains.
  Log: `/tmp/premise-reporting-console-build.log`.
- The full console `vitest run` passed **58 tests across 15 files**.
  Log: `/tmp/premise-reporting-console-suite.log`.

Real-browser end-to-end and accessibility qualification remain required, along
with the previously listed map/provider, bulk, cleanup and fleet checks. These
component tests do not establish browser or production-image acceptance.

### Browser and active-purge qualification — 2026-09-07

`bash tools/e2e-stack.sh reports.spec.ts --project=<engine>` passed separately
for **Chromium, Firefox and WebKit**, each against a freshly migrated database,
real API and built console. The test signs in, creates a site, selects it in the
report form, generates a real PDF, downloads it and reopens its request after a
reload. Axe checks at selection and completion found no serious/critical WCAG
2 A/AA violations in either theme. Logs:

- `/tmp/premise-reporting-browser-second.log` (Chromium)
- `/tmp/premise-reporting-browser-firefox.log`
- `/tmp/premise-reporting-browser-webkit.log`

The first Chromium check found insufficient primary-button hover contrast in
dark mode. The shared button hover background now mixes toward black in light
mode and white in dark mode instead of reducing opacity toward the underlying
surface. The rerun and the other engines passed. The completed-page screenshot
was inspected. Downloaded PDFs and screenshots from Firefox/WebKit were retained
under `/tmp/premise-reporting-browser/{firefox,webkit}/`; their actual PDF text was
checked for the generated site identity, title and optional-map warning. This is
the no-provider reference path, not full map or bulk visual qualification.

Active organization purge exposed a local storage edge case: `File.Delete`
throws when an artifact intent's parent directory has never existed. The local
adapter now treats only `DirectoryNotFoundException` as already absent, keeping
permission and other I/O failures visible for retry. The regression test starts
with one completed PDF and one held renderer, purges the organization's reports,
waits for the renderer to drain, runs maintenance, and proves both job/intent
removal and deletion of the completed PDF's bytes.

- `/tmp/premise-reporting-purge-fixed.log`: **13 reporting/file-trash tests passed**.
- `/tmp/premise-reporting-purge-final.log`: strengthened active-purge test passed.
- `/tmp/premise-reporting-storage-regression.log`: **8 storage tests passed**.

Remaining acceptance includes configured map/provider coverage, overlay access
revocation, more failure/orphan cases, the 100-PDF batch, multi-replica process
death/recovery, final Linux production-image checks and completed fork guidance.
No fleet, 100-PDF or production-readiness claim follows from these browser tests.

### 100-PDF reference batch — 2026-09-07

`dotnet test tests/Premise.IntegrationTests --filter FullyQualifiedName~One_hundred_reference`
passed using the production `site` definition, real PostgreSQL, durable execution,
local object storage, and the requester download endpoint. It produced 100 PDFs
and one ZIP with a 100-item manifest. Every PDF was reopened with PDFsharp. An
independent pypdf pass matched all 100 distinct captured site IDs to the text of
their corresponding PDFs and manifest entries. The manifest now includes each
item's captured site IDs, allowing that association without guessing filenames.

The final run took **6.556 seconds**, including submission, generation, bundle
download and verification; the ZIP was **2,262,005 bytes**. The test-host peak
working-set counter was unavailable and is recorded as null, not zero memory.
This run has no photos or external basemap, no CPU/memory container quota, and
executes inside one integration host. It proves this bounded functional workload,
not production capacity, image-heavy performance or replica scaling.

Evidence: `/tmp/premise-reporting-100-final.log`; artifacts and metrics in
`/tmp/premise-reporting-100/`. The runnable test is
`One_hundred_reference_pdfs_form_a_complete_downloadable_bundle`.

A dedicated `bash tools/replica-stack.sh 2 --reporting` mode now runs the destructive
report recovery case separately from the existing fleet test that kills an ingest
replica. Its first run found that ready PDFs were listed but downloads returned
404 until the whole batch obtained an expiration date. Active jobs now permit
ready-PDF downloads with the same requester/resource authorization and a 60-second
URL lifetime. Terminal-job expiration checks remain unchanged. Fleet recovery
results are recorded below; creating this test alone was not recovery evidence.

The second fleet run passed its actual recovery test: two APIs and two workers,
100 sites created through the API, a generating API killed after completed PDFs
appeared, then automatic lease/sweep recovery to all 100 PDFs. SHA-256 verified
that a PDF downloaded before process death retained identical bytes afterward.
The recovered ZIP had 100 PDFs plus the successful manifest, with no duplicate
ready artifacts. Submission through recovered-bundle verification took **177.148
seconds**; ZIP size was **2,198,011 bytes**. This measures one local recovery
scenario and includes the deliberate lease delay, not report rendering throughput.

Log: `/tmp/premise-reporting-fleet-second.log`; recovered ZIP and metrics retained
in `/tmp/premise-reporting-fleet/`. The test passed, but the shell wrapper then
exited 127 because its comments were edited while Bash was reading the running
file. A clean rerun of the unchanged wrapper is pending. The current topology
still allows report execution on API processes; dedicated worker-only routing and
Linux production-image qualification remain separate deployment work.

The complete reporting workflow regression selection passed **13 tests** after
the manifest and active-download changes (`/tmp/premise-reporting-bulk-regression.log`).

### Map composition, visual access and clean fleet rerun — 2026-09-07

The clean fleet wrapper rerun exited successfully. Its reporting process-death
test again passed with two APIs and two workers; log:
`/tmp/premise-reporting-fleet-final.log`. The previously described wrapper failure
does not apply to this run. Updated recovery metrics and ZIP are retained under
`/tmp/premise-reporting-fleet/`.

Basemap HTTP now uses the standard named `IHttpClientFactory` client
`report-basemaps`. Its production handler retains validated public-address
connections and no redirects, proxies or cookies. HTTP client request loggers
are disabled so configured URL credentials do not enter routine request logs.
The test host substitutes deterministic raster responses at that standard HTTP
boundary; the actual configuration lookup, decoder, map composition, report
definition, tenant database and download authorization still execute.

Verification covers successful XYZ tile composition with a site marker,
multipolygons and a polygon hole; unavailable providers; redirect responses; and
malformed images. Removing an included overlay blocks the generated report's
download and a new request selecting that overlay. Corrupt optional photographs
produce a warning and are excluded from dependencies; quarantining such an
unembedded photograph does not block the PDF. Included photographs remain subject
to current clean-file authorization.

The corrupt-image case found `InvalidDataException` missing from the optional
visual catch filters. Both map and photo paths now handle it as a warning while
authorization errors continue to fail. Visual inspection also moved map headings,
images, attribution and captions together across page breaks. The final two-page
fixture PDF was rendered with Poppler and both pages inspected: map, marker,
polygon hole, attribution and layer label are visible without clipping.

- `/tmp/premise-reporting-map-final.log`: four map cases passed.
- `/tmp/premise-reporting-visual-final.log`: 26 image/font/map/photo checks passed.
- `/tmp/premise-reporting-visual-regression.log`: six final map/photo cases passed,
  including corrupt-photo behavior and final pagination.
- Representative PDF: `/tmp/premise-reporting-map/site-map.pdf`.

These tests do not contact a live map provider or prove its licensing, DNS/TLS
availability or credentials. A live check requires a deployment-configured HTTPS
XYZ provider permitted for PDF use, its attribution and any credentials. The
existing address-policy checks remain separate from the synthetic HTTP fixture.

### Linux image qualification — 2026-09-07

The CI-style Release build and `codegen write` succeeded, followed by
`dotnet publish src/Premise.Api -c Release -r linux-x64 -p:PublishProfile=DefaultContainer -p:ContainerImageTag=reporting-verify -p:Version=0.0.0-reporting`.
The image was published only to the local Docker daemon; no registry push or
cloud deployment occurred.

`bash tools/smoke-image.sh premise:reporting-verify` passed all three roles in
Production mode against real PostgreSQL. API and worker ran non-root and passed
liveness/readiness; the worker completed a durable cleanup before the API started.
The resource-limited proof then ran in this image's runtime: eight pages in
1.170 seconds, peak working set 129,208,320 bytes (~123 MiB), 109,758-byte ZIP.
Its verifier passed PDF text, pages, embedded fonts/images and archive checks.

Logs: `/tmp/premise-reporting-image-build.log`,
`/tmp/premise-reporting-image-codegen.log`,
`/tmp/premise-reporting-image-publish.log`,
`/tmp/premise-reporting-image-smoke.log`, and
`/tmp/premise-reporting-image-proof.log`. Proof artifacts:
`/tmp/premise-reporting-image-proof/`.

The smoke verifies role startup and durable execution, while the proof loads
newly published proof binaries into that runtime. It does not yet exercise a
production reference-report job inside the image against live storage/provider
adapters. That distinction remains an acceptance gap, alongside worker-only
report routing, additional crash-window/race checks and finished fork guidance.

The complete reporting workflow/persistence selection passed **24 tests** after
the map changes (`/tmp/premise-reporting-map-workflow.log`).
The sequential architecture rerun passed **54 tests**
(`/tmp/premise-reporting-map-architecture-final.log`); the first attempt collided
with the integration build writing the shared API dependency file.

## Fork authoring and entitlement configuration

Start from [SiteReportDefinition](../src/Modules/Premise.Modules.Reporting/SiteReportDefinition.cs)
or [AggregateReportDefinition](../src/Modules/Premise.Modules.Reporting/AggregateReportDefinition.cs).
Implement [IReportDefinition](../src/Modules/Premise.Modules.Reporting/IReportDefinition.cs)
and register the concrete type with scoped DI:

```csharp
services.AddScoped<IReportDefinition, MyApplicationReport>();
```

Register it before Wolverine builds its handlers. Keep registration by concrete
type, matching the application's code-generation rules. The registry rejects
duplicate IDs at startup. `Aggregate = false` participates in single and bulk
requests; `Aggregate = true` receives all captured site IDs for one aggregate PDF.
The console discovers registered types. Fork types initially receive the JSON
options editor; add a feature-local options form when the application needs one.

Implement the contract as follows:

1. Give the definition a stable ID, display name and integer version. Validate
   options with a bounded, strict parser; return a useful validation error rather
   than silently ignoring unknown fields. The API also caps options at 16 KiB.
2. Authorize using the explicit request organization, user and site IDs. At
   admission/generation the dependency argument is null; check the resources the
   options intend to include. At download it contains the resources actually
   included in the saved PDF. Recheck those resources' current access and state.
   Return false on access loss. Optional-image fallback must never hide an
   authorization failure.
3. Read application data through established contracts or the owning module's
   permitted data access. Read fresh data when rendering; the captured selection
   is not a data snapshot. Bound rows, image sizes and geometry complexity.
4. Write a complete PDF to the supplied stream with PDFsharp/MigraDoc. Do not
   dispose that stream or create a separate job queue. Observe cancellation during
   I/O and expensive loops. The executor limits output bytes and execution time.
5. Return only the protected resource IDs actually included, plus user-safe
   warnings. A required visual failure must fail the item; an optional visual
   may become a placeholder and warning. Never put provider URLs or secrets in
   warnings. See `ReferenceReportAccess` and `ReferenceReportRenderer` for the
   implemented photograph/overlay authorization and fallback behavior.

The executor owns selection capture, reservations, charging, attempts, storage,
ZIPs, manifests and retention for every registered definition. One successful PDF
item costs one quota unit, including an aggregate; a fork does not record another
usage event from `RenderAsync`. Retries and duplicate delivery retain the item's
identity, which prevents successful PDFs from being charged again.

The current registry registers one version per ID. Jobs pin that version; replacing
it with a different version makes old queued work fail as unavailable and prevents
old artifact downloads. To overlap deployments while retaining old downloads,
register the new implementation under a new ID and keep the old definition until
its accepted jobs and artifacts have drained. Do not silently render an old job
with a new incompatible definition. This is a documented template limitation,
not side-by-side version support.

The bundled Liberation Sans resolver covers the qualified English/Latin layouts.
A custom global resolver must be installed before the first render and continue
to resolve the reference font family if reference layouts remain registered.
Retain the bundled font and library license notices in published images. Qualify
additional scripts, fonts and layout changes with rendered output, not text-only
PDF assertions.

`reports.enabled` and `reports.monthly` use the existing entitlement catalog,
operator assignments, exceptions and downgrade preflight. Set them through the
operator entitlement API or operator console; organization users cannot raise
their own allowance. The reporting page displays remaining, consumed and reserved
PDF counts. HTTP 402 indicates a plan restriction; 429 indicates the separate
operational job queue limit; 503 indicates transient admission contention. Retry
an uncertain submission with its original idempotency key and unchanged body.

Deploy an API publisher and at least one worker listener using the shared
PostgreSQL transport and the same private object store. Local disk storage is
only shared when all processes mount the same filesystem; independently deployed
containers need the configured shared object-store adapter. API/worker roles use
`app_user`; the migrate role applies the reporting schema. Provision the report
basemap separately with its allowed HTTPS XYZ template and attribution, as in the
configuration example above. A synthetic tile fixture does not verify real DNS,
TLS, provider credentials or redistribution terms.

## Related documentation

- [README](../README.md)
- [Production conventions](production.md)
- [Software maturity and verification](software-maturity-review-details.md)
- [Performance evidence](performance-and-scalability-assessment.md)
- [Architecture decisions](decisions/README.md)

### Entitlement gate and retry admission verification

The `reports.enabled` catalog entitlement now gates submission and explicit
retry through the existing entitlement service (402 when disabled). Existing
permission checks still apply. Cancellation and previously accepted downloads
remain available. The generated TypeScript entitlement catalog includes the key.
Strict monthly count accounting and its console presentation are still pending;
this feature switch does not establish quota enforcement.

- `dotnet test tests/Premise.IntegrationTests --filter 'FullyQualifiedName~Reporting_entitlement|FullyQualifiedName~Retrying_failed_work' --nologo`:
  **2 passed**, log `/tmp/premise-reporting-entitlement-gate.log`.
  Covers disabling submission/retry, retained downloads, re-enabled retry, and
  shared operational queue admission on retry. The intentional failure uses a
  fresh site because the test renderer tracks attempts per site.
- `dotnet run --project tools/Premise.Tools.CodegenKeys`: passed; generated keys
  updated from the catalog.
- These checks run with the new PostgreSQL report queue in the combined Testing
  host. They do **not** prove worker-only execution in a deployed fleet. The
  fleet crash test must identify and kill the executing worker before that claim
  is qualified again. Earlier fleet evidence describes the previous topology.
- The browser harness now starts a worker after the API becomes ready, avoiding
  concurrent Development seed initialization. Browser requalification remains
  pending after this deployment change.
- `dotnet test tests/Premise.ArchitectureTests --nologo`: **54 passed**, log
  `/tmp/premise-reporting-entitlement-architecture.log`.

### Strict quota implementation verification

- Quota admission, partial failure/retry accounting, cancellation release,
  concurrent last-slot admission, metadata-independent charges, entitlement
  disable/re-enable, and operational retry admission: **6 tests passed** in
  `/tmp/premise-reporting-quota-tests.log`.
- Reporting persistence, prior-month settlement, cross-tenant quota read/write
  isolation, actual single/aggregate PDF accounting, OpenAPI snapshot, RLS
  coverage and all-module migration round trips: **22 tests passed** in
  `/tmp/premise-reporting-quota-schema.log`.
- Migration SQL reviewed at `/tmp/premise-reporting-quota.sql`: indexed tenant/
  month and tenant/job queries, forced RLS, no job cascade for accounting rows.
- Generated OpenAPI, TypeScript client types and entitlement keys updated.
  Console TypeScript check passed; reporting component tests **10 passed**,
  including disabled-plan and exhausted-quota states.
- Current concurrency evidence is concurrent requests to one integration host
  backed by PostgreSQL. Quota admission across separate API replicas and crash
  recovery accounting still need fleet qualification. The prior-month test
  settles a stored reservation from the previous month; it does not simulate
  clock changes across a running fleet.
- Full reporting workflow and persistence regression: **31 passed**, including
  the 100-PDF scenario, in `/tmp/premise-reporting-quota-regression.log`.
- Architecture suite: **54 passed** in
  `/tmp/premise-reporting-quota-architecture.log`.
- Full console suite: **60 passed** in `/tmp/premise-reporting-quota-console.log`.
  The catalog-label check caught missing display labels for the new entitlements;
  both are now supplied for existing billing/operator screens.

### Worker-only fleet and shared quota qualification

`bash tools/replica-stack.sh 2 --reporting` passed **2 tests**, with a clean
wrapper exit, in `/tmp/premise-reporting-quota-fleet.log`:

- Twelve concurrent submissions reached multiple API replicas with one quota
  slot available; exactly one was admitted and charged.
- The crash test matched the actual report claim to a worker PID recorded by
  the harness, killed that worker after ready PDFs existed, and recovered all
  100 PDFs through the survivor. An earlier PDF retained its SHA-256 hash.
  The final accounting increased consumed usage by exactly 100 and left no
  extra reservations. Both API replicas remained available.

The first attempt stopped before killing a process because worker health probes
have no instance header. The harness now passes its recorded worker PIDs rather
than inferring the worker from the submitting API or changing production probes.

Recovery took **174,960 ms** including the deliberate lease delay; the ZIP was
**2,198,770 bytes**. This is a local recovery qualification, not a throughput or
resource-capacity measurement. The recovered archive and metrics are retained at
`/tmp/premise-reporting-quota-fleet/`. A separate pypdf pass reopened all 100 PDFs
and found the generated fleet site content. Process death during storage
completion or ZIP assembly still requires separate evidence.

Documentation review also found that the options ceiling counted UTF-16
characters rather than UTF-8 bytes. Admission now measures UTF-8 bytes; the
unescaped multibyte-options regression passed in
`/tmp/premise-reporting-options-boundary.log` without reserving quota.

### Browser allowance and plan lifecycle verification

- `bash tools/e2e-stack.sh reports.spec.ts`: Chromium, Firefox and WebKit all
  passed with separate API and worker processes, report generation/download,
  remaining allowance, and accessibility checks in both themes. Log:
  `/tmp/premise-reporting-quota-browsers.log`.
- The first browser attempt caught an inherited API launch profile binding the
  worker to the API port. The harness now uses `--no-launch-profile`, a separate
  configurable worker port, and a worker health gate before browser startup.
- A strengthened WebKit check also waited for **1 generated, 0 reserved**, then
  captured the completed page at the top of the document. It passed in
  `/tmp/premise-reporting-quota-browser-settlement.log`; the inspected screenshot
  and downloaded reference PDF are in `/tmp/premise-reporting-quota-browser/`.
- The existing subscription lifecycle integration test now verifies reporting
  quotas through Growth, Scale and cancellation: **1 passed** in
  `/tmp/premise-reporting-plan-quota.log`. Plan values are 5,000 and 50,000;
  cancellation returns to the 1,000 default through the existing plan-row logic.

The production image predates these quota and routing changes and must be rebuilt
and qualified again. Actual reference jobs in that image, storage-completion/ZIP
crash windows, and the remaining acceptance audit are still outstanding. No cloud
resources were provisioned and no commit or push was made.
- Existing plan-catalog invariants: **5 passed** in
  `/tmp/premise-reporting-plan-catalog.log`.
- Upgrade acceptance still needs an existing-paid-subscription scenario: the
  entitlement service currently reads assigned rows, then catalog defaults.
  Adding a new code to a plan bundle updates it on the next billing event but
  does not itself backfill already-active subscriptions. A safe backfill must
  preserve operator values and exceptions before this upgrade path is complete.

### Site-library generation and run navigation

Generation now opens from the selected library rows or a site detail page. The
dialog captures that selection, defaults multiple sites to separate PDFs and ZIP,
and offers an aggregate over the same IDs. Acceptance opens `/reports/<runId>`.
The Reports index lists run IDs, and each run links back to its included sites.
The existing server entitlement checks and PDF accounting remain in force.

The library owns the dialog state outside its loading/selection footer. A browser
check caught a debounced search replacing that footer and discarding the open
dialog; moving the state fixes that refresh race without changing the selection.
The console suite passes **62 tests** in `/tmp/premise-reporting-site-flow-ui.log`,
and the console TypeScript check passes.

Open-dialog WebKit verification exposed Base UI's internal focus sentinels to
axe. The [maintainer explains their intentional Safari/VoiceOver role](https://github.com/mui/base-ui/issues/5237#issuecomment-4978602561)
and recommends excluding those internal elements. The shared accessibility helper
excludes only `span[data-base-ui-focus-guard]`; it still scans application controls.
The reporting browser scenario also checks keyboard focus wrapping in both
directions, generation, allowance settlement, PDF download and run URL reload.
This does not constitute manual VoiceOver qualification.

`bash tools/e2e-stack.sh reports.spec.ts` now passes in **Chromium, Firefox and
WebKit**, with a clean wrapper exit, in
`/tmp/premise-reporting-site-flow-browsers.log`. The inspected completed-page
screenshot and downloaded one-page reference PDF are retained at
`/tmp/premise-reporting-site-flow/`; pypdf reopened the PDF and verified its site
content. Initial failures caught the footer refresh race, a missing semantic run
heading, the internal WebKit sentinel finding and an incorrect exact-label test
selector; these are resolved in the passing run.

At this earlier checkpoint, durable site-file publication and file-to-run links
were still outstanding. Its browser evidence covers the earlier private-artifact
flow; the shared site-file implementation and newer evidence follow below.


### Shared site-file publication

The accepted access change is implemented through the existing Storage lifecycle.
`FileObject` records `SiteIds`, `Origin`, and `OriginId`. One aggregate PDF has one
file ID and object, associated with every included site. PDFs use the artifact ID
as their stable file ID; `OriginId` is the report run ID. Files and run views use
current file-read/site-read scope and definition-specific dependency checks.
Generating/retrying still requires the reporting entitlement and report permission;
publication acknowledgments, downloads, links and retries of successful outputs do
not reserve or consume additional quota.

A successful item and its `PublishGeneratedFile` message commit through the
Reporting database/outbox transaction. Storage inserts the Clean file idempotently
and acknowledges publication. The run exposes `fileId` after acknowledgment;
unacknowledged PDFs show “Publishing” and continue polling. Maintenance reconciles
unacknowledged and pre-upgrade ready PDFs. Redelivery never restores trashed or
erased files or resets legal hold. Storage serializes publication against an
organization purge fence, so delayed messages cannot recreate purged files.

`IFileOriginAccess` is a host-wired Platform authorization port, analogous to the
existing centralized scope ports. Storage read access fails closed for an unregistered origin;
the report adapter rechecks the original definition version with the **reader’s**
identity and captured dependencies. Storage does not reference the Reporting
assembly. Forks retain definition versions needed by stored files; removing a
version makes those files inaccessible until the version is restored or content
is explicitly migrated. Generic file metadata, download, hold, trash and restore
all cross the same file-access check. Raw ingest/photo-source lookups exclude
origin-protected files; they cannot obtain report bytes through another module.

File lists accept `siteId`, check every association (not just the requested site),
and evaluate producer authorization for a bounded candidate page. `total` is now
nullable and returned as null; `nextOffset` advances through candidate pages, so a
page may be empty while further authorized files remain. This avoids exposing
unauthorized file metadata or scanning all historical dependencies for a count.
Large-library list throughput has not been qualified by these functional tests.

Report expiration removes ZIPs and failed/unready attempts, never a ready PDF's
bytes. Run/artifact provenance remains with file tombstones until org offboarding.
The normal file trash sweep owns PDF erasure. A stored ZIP is refused after any
included PDF is trashed or erased; it cannot retain a bypass to deleted content.
Run viewing requires the full selection; an individual site PDF can remain readable
when the reader no longer has access to other sites in its originating bulk run.


Shared site-file verification (2026-09-07):

- Initial publication/sharing/retention and OpenAPI checks: **4 passed**, in
  `/tmp/premise-site-files-first-tests.log`.
- Reporting persistence, RLS and migration round-trip pass in the earlier
  44-test regression (**43 passed**, one old 403 expectation failed because the
  common file path intentionally returns 404). That expectation was corrected.
- Reporting workflows, shared and scoped readers, aggregate associations, original
  file operations and purge publication fence: **39 passed**, in
  `/tmp/premise-site-files-tests.log`.
- Final redelivery, ZIP trash/expiry, scoped aggregate reader, purge-fence,
  trash-hold and OpenAPI checks: **7 passed**, in
  `/tmp/premise-site-files-lifecycle.log`.

The shared trash sweep now honors holds placed after a file enters Trash and
serializes with hold/trash/restore changes using the existing aggregate lock.
The regression places a hold in Trash, runs the aged-file sweep, verifies bytes
remain, releases the hold and verifies erasure. Normal organization offboarding
still deliberately overrides holds.

- Architecture checks: **54 passed**, `/tmp/premise-site-files-architecture.log`.
- Console checks: **63 passed**, `/tmp/premise-site-files-console.log`; TypeScript
  check passed. The Files view lives in its feature and is reused on site detail;
  read-only users can view runs without querying generation allowance.
- Scoped candidate-page continuation: **1 passed**,
  `/tmp/premise-site-files-pagination.log`. An unauthorized aggregate can leave
  an empty page; the following page still returns the authorized site PDF. The
  Files view offers explicit Load more in addition to virtual scrolling.
- Complete browser workflow: **Chromium, Firefox, WebKit passed**,
  `/tmp/premise-site-files-browsers.log`. The scenario generates from a selected
  site, downloads through both run and site Files, follows site/file/run links,
  opens the run from the global Files library, and checks accessibility in both
  themes and dialog keyboard focus wrapping. The shared internal Base UI sentinel
  exclusion described above remains the only accessibility exclusion.
- Inspected completed-page screenshot and readable reference PDF retained in
  `/tmp/premise-site-files-browser/`. The screenshot exposed an irrelevant ZIP
  expiry label for single reports; that label now appears only for bulk runs.
- Reviewed migration SQL: `/tmp/premise-site-files-storage.sql` and
  `/tmp/premise-site-files-reporting.sql`. The new purge-fence table has forced
  tenant RLS; existing file rows receive an empty site association and null origin.

These checks qualify the shared site-file workflow. They do not close the separate
production-image, live-provider, paid-subscription backfill or remaining targeted
crash-window acceptance gaps recorded earlier.


File management checks file-manage and site-read scope over every associated site.
It does not require reading the generated content: an authorized manager can still
hold/trash/restore a file after an included photograph is erased or a definition
version is retired. Restore does not bypass the separate read check; downloads
remain refused while those dependencies are unavailable.


Final shared-file acceptance:

- Management/read separation: **3 passed**,
  `/tmp/premise-site-files-management.log`. A missing included photo still blocks
  both download routes, while an authorized manager can trash/restore the file;
  a read-only or partly scoped user cannot gain management or aggregate access.
- `bash tools/replica-stack.sh 2 --reporting`: **2 passed**, clean wrapper exit,
  `/tmp/premise-site-files-fleet.log`. Two API replicas admitted exactly one
  concurrent last-slot request. After killing the actual worker processing a
  100-PDF run, the survivor recovered all PDFs, the ZIP and all **100 site-file
  records**, each with its site association and originating run ID. Previously
  published bytes retained their hash. Usage rose by exactly 100 with no extra
  reservations; both API replicas remained available.
- Recovery took **175,030 ms**, including the deliberate lease wait. The ZIP was
  **2,198,564 bytes**. This proves local crash recovery, not capacity or throughput.
  The archive and metrics are retained in `/tmp/premise-site-files-fleet/`;
  pypdf independently reopened all 100 PDFs and found reference site-report content.

The requested site-library/run-history/shared-file redesign is functionally
implemented and verified. The broader reporting goal still has the separate
production/deployment and upgrade qualifications listed above; it is not marked
complete by this checkpoint. No commit, push or cloud provisioning was performed.

### Deployment and recovery follow-through — 2026-09-07

The missing-subscription-code upgrade is now part of the migrate role (see the
upgrade order above). `PlanEntitlementBackfillTests` starts with 205 subscriptions
and their pre-reporting plan bundles: **199 missing report assignments inserted**,
then **zero changes** on a second pass. It covers Growth, Scale, trialing,
payment-grace and canceled subscriptions, unknown plans/statuses, preserved
operator/manual/plan assignments and timestamps, active exception precedence,
and unchanged subscription truth. No applied migration was edited.

Four targeted recovery cases reconstruct the durable database/object-store state
at a crash boundary, expire the old lease, and invoke the real maintenance/worker
path against PostgreSQL and local object storage:

| Boundary | Recovery assertion |
| --- | --- |
| PDF object stored, item/publication transaction not committed | Retry the unfinished item with a new artifact identity; preserve the earlier published PDF and charge each successful item once. |
| ZIP write interrupted | Rebuild a valid archive and manifest; do not regenerate successful PDFs or add report usage. |
| ZIP object stored, ready flag not committed | Publish one replacement bundle; retain the abandoned intent for expiry cleanup. |
| ZIP ready flag committed, run completion not committed | Reuse that same bundle ID and bytes; do not create a second published ZIP. |

The last case exposed duplicate ZIP creation in `ReportRunner.Complete`; it now
recognizes a ready bundle for the current revision. All four cases assert one
published ZIP, two site-file records, two consumed quota entries, zero outstanding
reservations, expected render attempts, an accurate manifest, and cleanup of
abandoned objects while preserving published PDFs. These are deterministic
crash-state recovery tests, not four additional OS process-kill tests. The separate
fleet test supplies the real worker-process-death evidence.

The production-image run exposed an additional S3-only bug: the AWS SDK's default
`AutoCloseStream = true` closed the caller-owned temporary report stream after
upload. Reading its length then raised `ObjectDisposedException`, so a stored PDF
was reported as failed. The shared S3 adapter now leaves input disposal to its
caller, matching local/Azure storage and the documented `IObjectStore` contract.
The MinIO regression checks that the input remains readable after upload.

Verification so far:

- Focused backfill and four crash boundaries: **5 passed**,
  `/tmp/premise-reporting-upgrade-crash.log`.
- Reporting workflow, shared file publication, billing and backfill regressions:
  **39 passed**, `/tmp/premise-reporting-final-regression.log`.
- S3 adapter and strengthened pre-upgrade subscription fixture: **3 passed**,
  `/tmp/premise-reporting-s3-upgrade.log`.
- Architecture: **54 passed**, `/tmp/premise-reporting-final-architecture.log`.

`bash tools/reporting-image.sh IMAGE [OUTPUT]` now qualifies actual single,
aggregate and 100-site bulk jobs from one OCI image. Its Python helper uses only
the standard library. A disposable Development API prepares users, 100 sites and
a session; it is removed before report submission. The image's API and worker
then run in **Production** with PostgreSQL and the real S3 adapter against MinIO.
The same session exercises authenticated submission, current read authorization,
presigned downloads, site-file association and quota settlement. This fixture
does not verify an external OIDC login or contact unused billing/KMS/SMTP adapters.

The harness verifies non-root processes and application-role DB connections,
uses a read-only root filesystem, and applies **1 CPU / 768 MiB per application
process**, with a 256 MiB temporary filesystem. It retains image identity, role
logs, completed run responses, PDF/ZIP outputs, submission-to-publication timings,
quota counts and a zero-dead-letter check. Its disposable containers, key volume,
network and fixture session cookie are removed on exit. CI runs it after the
existing three-role image smoke and uploads the evidence directory.

The corrected `premise:reporting-qualified` image passed this harness. Image ID:
`sha256:9d1294062b79a0bf01c6b18add7eed7b2ac56e074c183b8a907a768eb60853fe`.
The image was built as **Linux amd64** and executed on this **arm64 Mac under
Docker emulation**; these are local qualification observations, not production
latency promises or replica-capacity measurements.

| Reference run | PDFs | Submission through completed run and published site files | Download size |
| --- | ---: | ---: | ---: |
| Single site, first report on the fresh worker | 1 | 10.216 s | 24,500 bytes |
| Aggregate of 100 sites | 1, four pages | 1.846 s | 35,062 bytes |
| Separate reports for 100 sites plus ZIP | 100 | 16.187 s | 2,314,823 bytes |

Timings include queueing, generation, persistence and polling through publication;
downloads and subsequent per-site listing assertions occur afterward. They do
not separate rendering from ZIP assembly, and there is only one sample per mode.
The fresh-worker single result includes first-use costs. No photos or external
basemap were selected; reports explicitly show the missing-basemap warning.

The run consumed **102 PDFs**, left **0 reservations** and **898 of 1,000**
available, with **zero dead letters**. Every selected site listed exactly the
expected PDF for each run, including the shared aggregate file. All 100 bundled
PDFs were independently reopened with pypdf and matched to their distinct
manifest site IDs. All four aggregate pages and representative individual pages
were rendered with Poppler and visually inspected; headers, table continuation,
page numbers and text were legible. The inspected PDFs contain embedded Liberation
Sans fonts. Neither a mounted renderer helper nor host-installed fonts supplied
the production report implementation.

Durable local copies (ignored test output):
[image metrics](../coverage/reporting-follow-through-2026-09-07/image/metrics.json),
[single PDF](../coverage/reporting-follow-through-2026-09-07/image/single.pdf),
[aggregate PDF](../coverage/reporting-follow-through-2026-09-07/image/aggregate.pdf),
[100-site ZIP](../coverage/reporting-follow-through-2026-09-07/image/bulk-100.zip).
Logs and image identity are beside these artifacts; CI keeps its own uploaded
`reporting-image-evidence`. The unsuccessful harness setup and S3 diagnosis remain
under `/tmp/premise-reporting-image-final*`; they are superseded by the passing
`/tmp/premise-reporting-image-verified.log`.

The final `bash tools/replica-stack.sh 2 --reporting` rerun also passed **2 tests**
with a clean wrapper exit. Two API replicas admitted exactly one last-slot request.
The test then killed worker **7699** during a 100-site run; worker **7700** claimed
the expired lease and completed all 100 PDFs, their site-file records and the ZIP.
Previously completed bytes retained their SHA-256 hash, and usage increased by
exactly 100 with no extra reservations. Recovery took **174.197 seconds including
the deliberate lease wait**; the ZIP was **2,198,958 bytes**. This timing measures
failure recovery, not normal generation. The shell's `Killed: 9` line is the
expected injected failure, and `Terminated: 15` is proxy teardown.
See the retained [fleet metrics](../coverage/reporting-follow-through-2026-09-07/fleet/metrics.json)
and `/tmp/premise-reporting-final-fleet.log`.

These results close the requested local production-image, subscription-backfill
and targeted crash-window gaps. All test-owned containers, networks and key
volumes were removed. Shell/Python/workflow-YAML syntax checks and `git diff
--check` passed. External live-basemap credentials/licensing and deployment on the
chosen target infrastructure remain separate qualifications; no cloud resources,
commits or pushes were created by this work.
