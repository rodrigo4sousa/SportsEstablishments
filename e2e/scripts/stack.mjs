// Starts or stops the stack the E2E tests run against: postgres, mailpit, api and frontend from ../docker-compose.yml, in a project of
// its own (so a development stack and its volume are never touched), with the demo data.
//
//   node scripts/stack.mjs up     fresh database, build, start, wait until the site answers, seed the demo data
//   node scripts/stack.mjs down   stop and delete the project's containers and volume

import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, "..", "..");
const compose = [
  "compose",
  "-p",
  "sportsbooking-e2e",
  "-f",
  path.join(root, "docker-compose.yml"),
  "-f",
  path.join(root, "e2e", "compose.e2e.yml"),
];
const siteUrl = process.env.E2E_BASE_URL ?? "http://localhost:3000";

function docker(args, { allowFailure = false } = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn("docker", [...compose, ...args], { cwd: root, stdio: "inherit" });
    child.on("error", reject);
    child.on("exit", (code) => {
      if (code === 0 || allowFailure) resolve(code);
      else reject(new Error(`docker ${args.join(" ")} exited with ${code}`));
    });
  });
}

/** Waits for a request that goes through the site, the rewrite, the API and the database. */
async function waitForSite(timeoutMs = 180_000) {
  const deadline = Date.now() + timeoutMs;
  let last = "no answer";
  while (Date.now() < deadline) {
    try {
      const response = await fetch(`${siteUrl}/api/localities`);
      if (response.ok) return;
      last = `HTTP ${response.status}`;
    } catch (error) {
      last = error instanceof Error ? error.message : String(error);
    }
    await new Promise((resolve) => setTimeout(resolve, 2_000));
  }
  throw new Error(`The site did not answer in ${timeoutMs / 1000} s (${last}).`);
}

const command = process.argv[2];
if (command === "up") {
  await docker(["down", "-v", "--remove-orphans"]);
  await docker(["up", "-d", "--build", "--wait"]);
  await waitForSite();
  await docker(["run", "--rm", "api", "seed-demo"]);
} else if (command === "down") {
  await docker(["down", "-v", "--remove-orphans"], { allowFailure: true });
} else {
  console.error("Usage: node scripts/stack.mjs up|down");
  process.exit(2);
}
