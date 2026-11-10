// Checks, against the BUILT app (`npm run build` first), that unknown resources answer a real HTTP 404 and that a club that exists
// still answers 200. It starts the standalone server (`.next/standalone/server.js`) with a stub API, so it needs no database, no Docker and no browser.
//
//   node scripts/check-not-found.mjs
//
// Why this is a script and not a Vitest test: the status code is decided by Next.js itself (the proxy runs before the page streams), which
// only a running server shows. `src/proxy-not-found.test.ts` covers the proxy's own decisions.

import { spawn } from "node:child_process";
import { createServer } from "node:http";
import { setTimeout as sleep } from "node:timers/promises";

const SITE_PORT = Number(process.env.CHECK_SITE_PORT ?? 3210);
const API_PORT = Number(process.env.CHECK_API_PORT ?? 5210);
// "localhost", like the Host header a browser sends: the proxy rewrites to the request's own URL, and a different host would be treated as external.
const SITE = `http://localhost:${SITE_PORT}`;
const GUID = "0199f7a0-0000-7000-8000-0000000000aa";

const club = {
  slug: "clube-existente",
  name: "Clube Existente",
  address: "Rua do Teste 1",
  locality: "Porto",
  phone: "220000000",
  contactEmail: "geral@clube.example.test",
  pitches: [{ id: GUID, name: "Campo 1", sport: "padel", pricePerSlot: 20, hasPriceBands: false }],
};

let apiHits = 0;
const api = createServer((request, response) => {
  apiHits += 1;
  const url = new URL(request.url ?? "/", "http://stub");
  if (url.pathname === `/api/clubs/${club.slug}`) {
    response.writeHead(200, { "Content-Type": "application/json" });
    response.end(JSON.stringify(club));
    return;
  }
  response.writeHead(404, { "Content-Type": "application/problem+json" });
  response.end(JSON.stringify({ code: "club_not_found" }));
});
await new Promise((resolve) => api.listen(API_PORT, "127.0.0.1", resolve));

// The standalone server, as in the Docker image (`next start` does not work with `output: "standalone"`).
const site = spawn(process.execPath, [".next/standalone/server.js"], {
  env: { ...process.env, PORT: String(SITE_PORT), HOSTNAME: "0.0.0.0", API_INTERNAL_URL: `http://127.0.0.1:${API_PORT}`, NODE_ENV: "production" },
  stdio: ["ignore", "inherit", "inherit"],
});
let siteExited = false;
site.on("exit", () => {
  siteExited = true;
});

async function stop(code) {
  site.kill();
  api.close();
  process.exit(code);
}

async function waitUntilUp() {
  for (let attempt = 0; attempt < 60; attempt += 1) {
    if (siteExited) {
      throw new Error("the standalone server exited early (is `npm run build` done?)");
    }
    try {
      await fetch(`${SITE}/nope`, { redirect: "manual" });
      return;
    } catch {
      await sleep(1000);
    }
  }
  throw new Error("the site did not start in 60 s");
}

const failures = [];
async function expectStatus(path, expected, { headers = {}, text } = {}) {
  const response = await fetch(`${SITE}${path}`, { redirect: "manual", headers });
  const body = await response.text();
  const problems = [];
  if (response.status !== expected) {
    problems.push(`status ${response.status}, expected ${expected}`);
  }
  if (text && !body.includes(text)) {
    problems.push(`body does not contain "${text}"`);
  }
  console.log(`${problems.length === 0 ? "ok  " : "FAIL"} ${expected} ${path}${problems.length ? ` -> ${problems.join("; ")}` : ""}`);
  if (problems.length) {
    failures.push(`${path}: ${problems.join("; ")}`);
  }
}

try {
  await waitUntilUp();

  // A club that exists: the happy path is untouched.
  await expectStatus(`/clubs/${club.slug}`, 200, { text: club.name });

  // A club the API does not have, and slugs that cannot exist: a real 404 with the site's not-found page.
  await expectStatus("/clubs/clube-inexistente", 404, { text: "Página não encontrada" });
  await expectStatus("/clubs/Not_A_Slug", 404, { text: "Página não encontrada" });

  // The owner and admin pages with an id that is not a GUID.
  await expectStatus("/manage/clubs/not-a-guid", 404);
  await expectStatus("/manage/clubs/not-a-guid/calendar", 404);
  await expectStatus("/manage/clubs/not-a-guid/statistics", 404);
  await expectStatus("/admin/owners/not-a-guid", 404);

  // A path that matches no route at all, as a control.
  await expectStatus("/nope", 404);

  // Prefetches and client-side navigations do not cost the API a request.
  const before = apiHits;
  await fetch(`${SITE}/clubs/clube-inexistente`, { redirect: "manual", headers: { rsc: "1", "next-router-prefetch": "1" } });
  if (apiHits !== before) {
    failures.push("a prefetch of a club page asked the API");
    console.log("FAIL a prefetch of a club page asked the API");
  } else {
    console.log("ok   a prefetch of a club page does not ask the API");
  }
} catch (error) {
  failures.push(String(error));
  console.error(error);
}

if (failures.length > 0) {
  console.error(`\n${failures.length} check(s) failed:\n- ${failures.join("\n- ")}`);
  await stop(1);
}
console.log("\nUnknown resources answer 404 and an existing club answers 200.");
await stop(0);
