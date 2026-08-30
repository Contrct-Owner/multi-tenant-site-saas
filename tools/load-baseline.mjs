#!/usr/bin/env node
// Zero-dependency load baseline (maturity review: "Performance UNMEASURED").
// Node 20+; global fetch. Numbers from a laptop are RELATIVE costs - endpoint
// vs endpoint, ceiling behavior - never capacity planning.
//
//   node tools/load-baseline.mjs <baseUrl> <bearerToken> [seconds] [concurrency]
//
const [base, token, secondsArg, concurrencyArg] = process.argv.slice(2);
if (!base || !token) {
  console.error('usage: node tools/load-baseline.mjs <baseUrl> <premise_key> [seconds] [concurrency]');
  process.exit(1);
}
const seconds = Number(secondsArg ?? 10);
const concurrency = Number(concurrencyArg ?? 16);

const targets = [
  ['healthz (pipeline floor)', '/healthz'],
  ['sites paged (limit 50)', '/api/sites?limit=50'],
  ['sites search', '/api/sites?q=site&limit=50'],
  ['site detail', null], // resolved from the list below
  ['members', '/api/members'],
  ['listings feed (full fleet)', '/api/listings/feed'],
  ['public sites (unpaged ceiling)', '/public/sites'],
  ['public sites near-sort', '/public/sites?near=42.36,-71.05'],
];

const headers = { authorization: `Bearer ${token}` };

async function measure(name, path) {
  const latencies = [];
  let errors = 0;
  const deadline = Date.now() + seconds * 1000;
  async function worker() {
    while (Date.now() < deadline) {
      const started = performance.now();
      try {
        const res = await fetch(base + path, { headers });
        await res.arrayBuffer();
        if (!res.ok) errors++;
        else latencies.push(performance.now() - started);
      } catch {
        errors++;
      }
    }
  }
  // warmup
  for (let i = 0; i < 5; i++) await fetch(base + path, { headers }).then((r) => r.arrayBuffer());
  const t0 = Date.now();
  await Promise.all(Array.from({ length: concurrency }, worker));
  const elapsed = (Date.now() - t0) / 1000;
  latencies.sort((a, b) => a - b);
  const pct = (p) => latencies[Math.min(latencies.length - 1, Math.floor((p / 100) * latencies.length))]?.toFixed(1);
  console.log(
    `${name.padEnd(32)} rps=${(latencies.length / elapsed).toFixed(0).padStart(6)}  p50=${pct(50)}ms  p95=${pct(95)}ms  p99=${pct(99)}ms  errors=${errors}`,
  );
}

// resolve one site id for the detail target
const sites = await (await fetch(`${base}/api/sites?limit=1`, { headers })).json();
const siteId = sites.items?.[0]?.id;

console.log(`base=${base} duration=${seconds}s concurrency=${concurrency}\n`);
for (const [name, path] of targets) {
  const resolved = path ?? (siteId ? `/api/sites/${siteId}` : null);
  if (!resolved) continue;
  await measure(name, resolved);
}
