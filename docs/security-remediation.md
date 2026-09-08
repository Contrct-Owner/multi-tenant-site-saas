# Security remediation

This ledger tracks every finding in [the security assessment](../security_best_practices_report.md). Changes are isolated in `codex/security-remediation`, based on `feat/performance-optimizations` at `b9f2fe8`. Commit `6e7b0aa` preserves the existing uncommitted Reporting/Storage work and assessment as the remediation baseline. The original checkout was not edited by these fixes.

Source changes do not establish the security state of an existing deployment. The verification section records tests actually run; the deployment section identifies prerequisites that require the deployed environment.

## Findings and resolutions

| ID | Resolution in this branch | Verification / operational boundary |
|---|---|---|
| SEC-01 | Settings require organization-wide `org:manage`; writes validate size and reject the typed `map.*` namespace. Hierarchy reads require `sites:read` and return the authorized subtrees plus their ancestors. | Guest, role-free member, owner and tenant-isolation regressions; scoped hierarchy checks. |
| SEC-02 | Leaflet tooltips receive a DOM node populated through `textContent`. | Malicious names remain literal text in unit and built-browser checks. |
| SEC-03 | Protected OAuth state includes a random browser correlation value in an HttpOnly, SameSite cookie; callback validates age and correlation and deletes the cookie before exchanging the code. | Fresh-browser callback rejection and replay rejection; normal login regression. |
| SEC-04 | Idempotency storage keys include organization, user and session; fingerprints include query strings. Expired records cannot replay. Responses are retained only for explicit policies which recheck current authority and resource visibility; one-time secrets are never retained. Other completed retries return 409 without repeating the mutation. | Cross-user secret-response regression, same-session secret redisclosure rejection, settings replay/conflict and report submission replay. See contract clarification below. |
| SEC-05 | Exception evaluation requires current membership; member removal/leave also delete supplementary exceptions. | Legacy orphaned exception fails closed, even when membership is removed directly in the database. |
| SEC-06 | The shared scope resolver refuses subtree grants for organization-wide administration: roles, org, audit, entitlements, ingest and platform operations, including service keys. | Scoped administrator cannot grant wildcard authority or exercise org-wide management. Owners retain their normal workflows. |
| SEC-07 | Ingest requires validated HTTPS destinations, public addresses checked at connection time, pinned socket addresses, no proxy/cookies/redirects, and replacement credentials when origins change. | Public/private/mapped-IP and redirect tests. Explicit Development/Testing loopback HTTP remains available for local fixtures. |
| SEC-08 | Audit webhook delivery uses the same connection-time destination policy, with redirects disabled; delivery does not buffer remote response bodies. | Transport boundary regressions; webhook lifecycle tests. |
| SEC-09 | Export files carry producer origins and require the original privileged capability; legacy export names are recognized conservatively. | Files-only readers cannot list/download modern or legacy audit/org exports. Legacy-name matching may conceal similarly named ordinary files; rename those via a controlled migration if necessary. |
| SEC-10 | All ingest operations require entire-organization authority; shared resolver enforces the same restriction for service principals. | Scoped ingest denial and normal ingest tests. |
| SEC-11 | Public SSR relays only the session cookie and framework chunks, preserves security/expiry attributes, removes Domain, and floors Secure in production. | Cookie relay tests, including chunked cookies. |
| SEC-12 | Public site identifiers must be UUIDs before URL interpolation or upstream access. | Traversal/invalid identifiers rejected without an upstream request. |
| SEC-13 | Public API responses use `private, no-store` because content depends on the resolved principal and tenant. | Cache-control regression. Shared CDN caching requires a separately designed public-only authority/cache key. |
| SEC-14 | Anonymous signup only redirects to hosted signup; it no longer provisions a provider user. Administrative provisioning sets `EmailVerified=false`. | Signup and WorkOS lifecycle regression. |
| SEC-15 | Non-sliding cookies plus stored session creation enforce a 12-hour absolute lifetime, including cookies reissued by org switching. Verified WorkOS session-revoked, password-reset-succeeded and user-deleted events revoke local sessions for the corresponding provider user through the event timestamp. Support impersonation rechecks current platform authority on each request. | Absolute-age test, signed provider-event tests and impersonation tests. WorkOS webhook delivery/subscriptions must be configured; see deployment checklist. |
| SEC-16 | Preserves the existing trash hold fix: select only unheld files, lock/reload/recheck before deleting; hold updates use the same lock. | Existing legal-hold trash regression retained and included in integration suite. |
| SEC-17 | S3 tickets sign Content-Length. Providers without enforceable limits (Azure SAS) use an authenticated, owner-bound, expiring upload relay that checks actual bytes before storage. Outstanding pending/uploaded/quarantined uploads are capped per organization; unfinished S3 deletion retains quarantine and its budget until ticket expiry; oversized completion remains inaccessible; hourly cleanup erases old unfinished/quarantined uploads after ticket expiry and honors holds. | S3 length rejection, Azurite relay size/replay tests and cleanup/budget checks. Lifecycle reconciliation remains required for cloud writes interrupted between provider and database operations. |
| SEC-18 | Bounds connector bytes, CSV/staged rows, connector/export queue admission, export query/archive sizes and spatial JSON vertices/rings/features before expensive geometry processing. Webhook response bodies are not read. Sitemap work has an overall deadline and row/page cap and does not return partial success. | Data-plane boundary tests, import/export tests and frontend deadline tests. Capacity remains bounded per process/org; deployment resource limits and provider quotas remain necessary. |
| SEC-19 | Forwarded headers require configured trusted proxy IPs/networks and constrained hostnames; guest tenancy reads only the processed Host. Non-development serving environments refuse wildcard AllowedHosts. | Known/unknown peer tests and production boot negatives. Operators supply real ingress peer ranges and allowed domains. |
| SEC-20 | Non-development serving roles require both shared Data Protection key storage and a private-key certificate to wrap keys at rest. | Boot-refusal tests; smoke mounts a temporary protected ring. Provision and rotate the real wrapping key independently of its ring. |
| SEC-21 | Credential-bearing provider overrides require HTTPS outside Development/Testing; Azure rejects plaintext/dev-storage endpoints; SMTP requires TLS. Development adapters require an explicit Development or Testing environment. | Production and Staging boot-refusal tests. |
| SEC-22 | Local Docker PostgreSQL port publications bind to 127.0.0.1. | Shell/static infrastructure regression checks. |
| SEC-23 | Runtime connection recipes and AppHost construct app-user credentials without handing owner connection strings to API/worker; migration credentials remain isolated. | Renderer secret separation checks and role smoke. Existing deployed credentials must still be inspected/rotated by operators. |
| SEC-24 | Rendered namespace network policy defaults deny and admits only required ingress, DNS/scanner access and explicit operator-supplied egress ranges. | Renderer positive/negative checks. Actual CNI enforcement and NFS export restrictions require deployment verification. |
| SEC-25 | Workflow actions use immutable SHAs, read-only default permissions and nonpersistent checkout credentials. | CI security source checks. |
| SEC-26 | Local tickets bind upload/download operation; upload publication is atomic and write-once, including after deletion; actual streamed bytes enforce the limit. Path containment uses a directory boundary. | Local ticket confusion, replay, oversize and containment regressions. |
| SEC-27 | Basemap UI explains that browser map keys are visible in tile requests and need provider domain restrictions and quotas. | Frontend test/typecheck/build. |
| SEC-28 | Renderer supplies runtime-default seccomp, read-only container roots and bounded writable scratch space, with non-root/no-escalation/drop-capability settings. | Renderer checks and hardened image role smoke. NFS deployments need CSI/PVC migration before enforcing Restricted Pod Security. |
| SEC-29 | CI captures container runtime versions, emits image SBOM/security evidence and gates HIGH/CRITICAL image vulnerabilities with a pinned scanner. | Source checks and image smoke; exact rebuilt artifact scanning is separately recorded below. Developer machine runtime servicing is an external environment action. |

## Contract clarifications

Idempotency still suppresses duplicate mutations across replicas through PostgreSQL. Security-sensitive response replay is no longer implicit: it requires an explicit current-authorization policy. Settings writes and report submissions retain replay, with permission/resource checks; other completed retries return 409 and require reconciliation through the resource's authorized read API. Integrators must retain one-time secrets from their original successful response. A lost one-time-secret response requires revoking/rotating the resource, never redisclosing it from a generic cache. This narrows ADR 29's broad replay promise to resolve SEC-04 under the requested security remediation; adding another replay policy requires tests proving current resource authority.

Azure upload tickets retain the existing URL/method/headers/expiry response shape, but their URL is a same-origin authenticated API path. The browser still performs PUT followed by completion. Azure SAS cannot enforce a byte ceiling, so exposing its create SAS would bypass that ceiling. The relay stages bounded bytes in `/tmp`, admits one active relay per process, has a two-minute deadline and serializes each file's upload with the existing transaction lock. Size scratch space and gateway request/concurrency limits accordingly. S3 and local uploads retain direct enforceable tickets.

Organization-wide administrative grants cannot be delegated to subtrees. Site/hierarchy operations that have a concrete scope keep subtree support. Stored subtree administrative assignments become ineffective and should be removed or deliberately replaced with organization-wide grants after review.

Export admission is shared across audit, self-service organization and operator-triggered organization exports. One queued/running export is allowed per tenant, with one worker export execution per process. Query materialization is capped at 100,000 rows and 4 MiB serialized data, and organization archives at 32 MiB uncompressed. Oversized organization exports fail without publishing; audit export truncation is recorded in its manifest. Delayed export messages recheck the existing organization-purge fence before writing.

## Additional assessment observations

- The console no longer persists private hierarchy data in sessionStorage; stale stored copies are removed. In-memory query state remains session-scoped.
- Public SSR has nonce-based CSP and route-aware framing rules; `/embed` remains embeddable. The console provides CSP in built HTML and preview-server headers; the production static host must supply the remaining HTTP headers.
- Development auth/billing/mail/SMS/storage/scanner/secrets adapters are restricted to explicit Development/Testing, including in Staging.
- Contact links remain short-lived public-read credentials. Their current privilege is intentionally unchanged; higher-privilege contact actions would require single-use/re-authenticated tokens.
- Storage download tickets now force attachment disposition for local, S3 and Azure objects. Deploy an isolated object origin for active untrusted content. Cloud bucket/CORS/IAM policies are deployment properties and cannot be certified by source inspection.

## Verification

Verification is in progress. Results and logs are retained under the ignored `coverage/security/` directory in this worktree; CI retains image evidence as an artifact.

| Check | Result |
|---|---|
| `dotnet build Premise.slnx --no-restore --disable-build-servers -m:1` | Passed, zero warnings/errors. |
| Architecture tests | 54 passed. |
| Platform unit tests | 68 passed. |
| Integration tests, serial collections | Final result pending. |
| Frontend tests | 99 passed (65 console, 34 public). |
| Frontend typecheck, build and lint | Passed. |
| Built public app in Chromium | Passed: hydration, literal malicious tooltip text, no injected image/script, nonce CSP without violations, embeddable map and private sitemap. Synthetic local upstream; no production service contacted. |
| CI gate tests | Passed, including 45 negative cases. |
| Infrastructure source checks / renderer / shell syntax | Passed: 33 immutable action pins, five loopback publications, secret separation, network/pod policy validation and scanner failure propagation. |
| `dotnet csharpier check .` | Passed. Two unformatted migrations inherited from the baseline are explicitly ignored to preserve their bytes; other migrations remain checked. |
| Current image: three-role smoke / vulnerability scan | Final result pending. |

Tests use real PostgreSQL, MinIO and Azurite for persistence/storage behavior. The serial test settings disable parallel collections and cap test threads at one to reduce local Docker resource pressure following the crash. The developer SDK/shared runtime is still 10.0.102/10.0.2; servicing the machine installation remains an operator task.

## Deployment acceptance

Before deploying, supply constrained `AllowedHosts`, explicit trusted proxy peers, the shared key path and wrapping certificate, HTTPS provider endpoints, TLS SMTP, and the renderer's actual database/provider/telemetry egress CIDRs. Subscribe the signed WorkOS endpoint to the documented [WorkOS events](https://workos.com/docs/events): `session.revoked`, `password_reset.succeeded`, `user.deleted` and existing directory-sync events. Alert on webhook delivery failures: successful delivery invalidates affected local sessions on their next request; the hard session age is the 12-hour fallback if delivery is unavailable. Revocation conservatively ends all of that user's local sessions created before the event, rather than storing provider bearer tokens or trying to match one provider session.

Validate the rendered controls on the actual ingress/CNI and storage platform: no externally reachable local test databases; no runtime owner credentials; blocked cross-namespace/internal egress; protected Data Protection ring and wrapping key; private object storage and bounded quotas; lifecycle/reconciliation for abandoned provider uploads; active-content attachment/isolation; backup restore; NFS export restrictions; malware definition freshness; telemetry authentication and security alert delivery. The branch does not deploy changes or establish these live controls.

The prior npm advisory query was blocked by automatic approval review because it would submit dependency metadata externally; it has not been retried or bypassed. CI image scanning and existing NuGet audit controls are distinct checks. No claim is made that an unexecuted advisory query or uninspected live cloud configuration passed.
