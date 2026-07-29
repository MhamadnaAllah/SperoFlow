#!/usr/bin/env node
/**
 * Dependency-light release smoke (Node 18+ fetch).
 * Does not require Playwright or a full browser.
 *
 * Env:
 *   APP_BASE_URL        default http://127.0.0.1
 *   KNOWLEDGE_BASE_URL  optional
 *   REQUIRE_STACK       if "1", fail when health is unreachable
 */
const appBase = (process.env.APP_BASE_URL || "http://127.0.0.1").replace(/\/$/, "");
const knowledgeBase = (process.env.KNOWLEDGE_BASE_URL || "").replace(/\/$/, "");
const requireStack = process.env.REQUIRE_STACK === "1";

const failures = [];

async function check(name, url, { accept = [200], headers = {} } = {}) {
  try {
    const res = await fetch(url, {
      method: "GET",
      redirect: "manual",
      headers,
      signal: AbortSignal.timeout(20000),
    });
    if (!accept.includes(res.status)) {
      failures.push(`${name}: expected ${accept.join("|")}, got ${res.status} for ${url}`);
      return res;
    }
    console.log(`OK  ${name} (${res.status}) ${url}`);
    return res;
  } catch (err) {
    const msg = `${name}: ${url} -> ${err.message}`;
    if (requireStack) {
      failures.push(msg);
    } else {
      console.warn(`SKIP ${msg}`);
    }
    return null;
  }
}

async function main() {
  console.log(`=== e2e-smoke APP_BASE_URL=${appBase} ===`);

  await check("app-live", `${appBase}/health/live`, { accept: [200] });
  await check("app-ready", `${appBase}/health/ready`, { accept: [200, 503] });

  // /metrics must not be public via Caddy
  await check("metrics-hidden", `${appBase}/metrics`, { accept: [404] });

  const probeId = crypto.randomUUID().replaceAll("-", "");
  const live = await check("request-id", `${appBase}/health/live`, {
    accept: [200],
    headers: { "X-Request-Id": probeId },
  });
  if (live) {
    const echoed = live.headers.get("x-request-id");
    if (echoed === probeId) {
      console.log(`OK  request-id-echo (${echoed})`);
    } else {
      console.warn(`WARN request-id not echoed (got ${echoed})`);
    }
  }

  // Security headers on a document response (landing via web or redirect)
  const home = await check("home", `${appBase}/`, { accept: [200, 301, 302, 307, 308] });
  if (home && home.status === 200) {
    const xfo = home.headers.get("x-frame-options");
    const xcto = home.headers.get("x-content-type-options");
    if (xfo && xfo.toUpperCase().includes("DENY")) {
      console.log("OK  x-frame-options");
    } else {
      console.warn(`WARN x-frame-options missing/weak: ${xfo}`);
    }
    if (xcto && xcto.toLowerCase() === "nosniff") {
      console.log("OK  x-content-type-options");
    } else {
      console.warn(`WARN x-content-type-options missing/weak: ${xcto}`);
    }
  }

  if (knowledgeBase) {
    await check("knowledge-health-hidden", `${knowledgeBase}/health/live`, { accept: [404] });
  }

  if (failures.length) {
    console.error("e2e-smoke FAILED:");
    for (const f of failures) console.error(`  - ${f}`);
    process.exit(1);
  }
  console.log("e2e-smoke passed (or skipped unreachable targets without REQUIRE_STACK=1).");
}

main();
