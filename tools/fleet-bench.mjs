#!/usr/bin/env node
// The load baseline against the replica stack: sign in as the seeded owner,
// mint an API key, and run tools/load-baseline.mjs through the proxy. One
// row per target; run it at 1, 2 and 4 replicas and compare (the scaling
// table in docs/scaling.md). Numbers from one laptop are RELATIVE: every
// replica shares the same cores and the same Postgres.
//
//   node tools/fleet-bench.mjs <proxyBase> [seconds] [concurrency] [replicas]
//
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const [base, seconds = '15', concurrency = '32', replicas = '?'] = process.argv.slice(2);
if (!base) {
  console.error('usage: node tools/fleet-bench.mjs <proxyBase> [seconds] [concurrency] [replicas]');
  process.exit(1);
}

// a cookie jar the size of the sign-in flow: follow the local provider's redirects by hand
const jar = new Map();
const cookieHeader = () => [...jar].map(([k, v]) => `${k}=${v}`).join('; ');
async function follow(url, init = {}) {
  for (let hop = 0; hop < 8; hop++) {
    const res = await fetch(url, { ...init, redirect: 'manual', headers: { ...init.headers, cookie: cookieHeader() } });
    for (const raw of res.headers.getSetCookie()) {
      const [pair] = raw.split(';');
      const eq = pair.indexOf('=');
      jar.set(pair.slice(0, eq), pair.slice(eq + 1));
    }
    if (res.status >= 300 && res.status < 400 && res.headers.get('location')) {
      url = new URL(res.headers.get('location'), url).toString();
      init = { method: 'GET' };
      continue;
    }
    return res;
  }
  throw new Error('too many redirects');
}

// the org quota would stop the bench after a minute's worth: the operator lifts it for the seeded org
const opMe = await follow(`${base}/auth/login?hint=operator%40premise.local&returnUrl=%2Fme`);
if (!opMe.ok) throw new Error(`operator sign-in failed: ${opMe.status}`);
const orgs = await (await follow(`${base}/api/operator/orgs`)).json();
const acme = orgs.find((o) => o.slug === 'acme-dev') ?? orgs.find((o) => !o.isPlatform);
const lifted = await follow(`${base}/api/operator/orgs/${acme.id}/entitlements/api.requests_per_minute`, {
  method: 'PUT',
  headers: { 'content-type': 'application/json', origin: base },
  body: JSON.stringify({ value: '100000000' }),
});
if (!lifted.ok) throw new Error(`quota lift failed: ${lifted.status} ${await lifted.text()}`);
await new Promise((r) => setTimeout(r, 16_000)); // every replica's quota cache expires
jar.clear();

const me = await follow(`${base}/auth/login?hint=alice%40acme.test&returnUrl=%2Fme`);
if (!me.ok) throw new Error(`sign-in failed: ${me.status}`);
const roles = await (await follow(`${base}/api/roles`)).json();
const roleId = roles.find((r) => r.name === 'Owner')?.id ?? roles[0]?.id;
const key = await follow(`${base}/api/api-keys`, {
  method: 'POST',
  headers: { 'content-type': 'application/json', origin: base },
  body: JSON.stringify({ name: `fleet-bench-${Date.now()}`, roleId }),
});
if (!key.ok) throw new Error(`api key failed: ${key.status} ${await key.text()}`);
const { secret } = await key.json();

console.log(`\n== ${replicas} replica(s), ${seconds}s x ${concurrency} concurrent, through ${base}`);
const baseline = path.join(path.dirname(fileURLToPath(import.meta.url)), 'load-baseline.mjs');
const run = spawnSync(process.execPath, [baseline, base, secret, seconds, concurrency], { stdio: 'inherit' });
process.exit(run.status ?? 1);
