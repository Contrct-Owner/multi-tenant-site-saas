# Scaling out: what two and four replicas prove

The production topology (`docs/production.md`) runs the api and worker roles
as replicas behind a load balancer. This page is the evidence that they can:
which per-process state was made fleet-safe, the suite that proves it, and
the load table at one, two and four replicas on one host.

## What had to change (2026-09-06, ADR 52)

| State | Before | Now |
|---|---|---|
| Org / user / API-key rate limit | In-memory fixed window per process: N replicas gave N times the quota | One `platform.rate_windows` row per (partition, minute), upserted per request; fails open |
| Org quota cache | Five-minute per-process cache; a quota change reached one replica | Fifteen seconds; every replica learns a change by expiry |
| Local object-store upload tickets | An in-memory dictionary on the replica that issued the ticket | Signed with the master key, redeemable on any replica (the dev adapter only; cloud adapters were already presigned URLs) |
| Sessions, idempotency keys, sweep leases, the outbox | Already in Postgres | Unchanged, now proven across processes |
| Plan-limit site count, cluster-tile cache | Per process, sixty seconds | Per process on purpose, listed in `docs/production.md` |

## The fleet suite

`tools/replica-stack.sh N` boots one Postgres, the migrate role once, N api
and N worker processes from the same build, and a round-robin proxy, then
runs `tests/Premise.FleetTests` through the proxy:

| Case | Proves |
|---|---|
| One session is answered by every replica | Sessions are database state; the proxy spreads a cookie's requests and every replica answers it |
| An idempotency key holds across replicas | The same key sent to different replicas creates one site; a mid-flight retry is a 409 |
| An org quota is one number across replicas | After a quota change, a fresh window lets the quota through fleet-wide, and every replica refuses on the shared counter (at most one stale request per replica while its cache expires) |
| Each sweep runs once per period across workers | With N workers ticking, `platform.sweep_runs` holds one claim per (sweep, period) |
| Messages survive the death of the replica that took them | A 400-row ingest batch is committed, the replica that answered is killed with SIGKILL, and the survivors apply every row from the durable queue |

Every case skips when `PREMISE_FLEET_URL` is unset, so `dotnet test` over
the solution never needs the stack.

## Load at one, two and four replicas

`tools/replica-stack.sh N --bench 15 32` signs in as the seeded owner, mints
an API key and runs `tools/load-baseline.mjs` through the proxy: 15 seconds
per target at 32 concurrent requests. One laptop, one Postgres, every replica
on the same cores: the numbers are relative and the ceiling is the host,
not the design. Fill the table from the run's output.

Run on 2026-09-06 (Apple Silicon laptop, Postgres 17 in Docker, seeded dev
org). Requests per second, then p50 / p99 in milliseconds.

| Target | 1 replica | 2 replicas | 4 replicas |
|---|---|---|---|
| healthz (pipeline floor) | 9,012 · 3.2 / 8.8 | 8,705 · 3.3 / 9.4 | 7,859 · 3.5 / 11.6 |
| sites paged (limit 50) | 732 · 38.5 / 130.7 | 636 · 41.9 / 167.5 | 509 · 54.4 / 199.6 |
| sites search | 692 · 39.3 / 138.1 | 630 · 43.3 / 158.8 | 593 · 46.9 / 153.4 |
| site detail | 724 · 35.7 / 152.5 | 675 · 38.1 / 162.6 | 635 · 43.1 / 152.0 |
| listings feed | 678 · 40.0 / 141.8 | 642 · 41.0 / 159.6 | 624 · 44.2 / 147.7 |
| public sites (paged) | 717 · 37.0 / 141.6 | 673 · 37.6 / 167.5 | 668 · 39.9 / 147.7 |
| public sites near | 634 · 43.3 / 144.0 | 596 · 45.8 / 157.0 | 551 · 51.0 / 163.5 |

Zero errors on every row at every size. (`members` is left out: it is a
humans-only endpoint and the bench signs in with an API key, so every call
is a correct 403.)

What the table says on this host: the api is not the bottleneck. Every
data-bearing target sits at 600-700 requests per second whether one, two or
four replicas serve it, because all of them share one Postgres and one set
of cores; adding replicas adds a little proxy and scheduling cost and no
throughput. That is the expected shape for a single machine and it is the
result that matters for the design: the replicas are interchangeable, the
shared state is shared, and the ceiling is the database. On a real
deployment the replicas get their own cores and the same table shows
whether Postgres or the api saturates first.

The first four-replica run failed on something else: eight processes
against the Postgres image's default hundred connections exhausted the
server (`53300: sorry, too many clients already`) and the shared limiter
failed open as designed. The stack now sizes `max_connections` and gives
each process its share of the pool - the budget `docs/production.md` asks
operators for, found the hard way.

Read it as: throughput that grows with replicas is CPU-bound in the api and
scales out; throughput that does not is bound by Postgres or by the host,
and the next conversation is about the database, not more replicas.
