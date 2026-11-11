# 0010 · End-to-end tests with Playwright, in CI, against docker compose

- **Status:** Accepted on 2026-11-10 (T08).
- **Date:** 2026-11-10.
- **Elements:** DEV-E2E, PL-E2E, DEV-COMPOSE, DEV-WORKFLOWS, X-GHA, X-MAILPIT.

## Context

Until now the critical flows were walked through by hand in the browser, before each merge, with the `docker compose` stack (see the [Stack](../../stack.md), "Tests"). The integration tests (Testcontainers) and the component tests (MSW) cover the rules and the screen, each on its own side; nothing guarantees that the site, the Next.js rewrite, the API and the database work together. We want **few** end-to-end tests, stable ones, that run in CI as a quality signal.

What the repository imposes:

- **A single origin.** The session lives in httpOnly cookies and the API checks the `Origin` (Development: `:3000` and `:5080`); the browser has to talk to the site, which forwards `/api/*`.
- **Demo data** (`seed-demo`) and Mailpit already exist in the `docker compose` stack. There is no email verification at registration, so no flow needs to read Mailpit.
- **`deploy.yml` waits for the `CI` workflow.** A slow or unstable step in `ci.yml` would delay or block the deploys.

## Decision

1. **Where the code lives: `e2e/`, at the root**, with its own `package.json` and `package-lock.json`, and not `frontend/e2e`. The tests import no code from the front end or the API (they only speak HTTP and the DOM), so there is no reason for the front end's `npm ci` and Dependabot to carry Playwright, nor for Playwright to be among the site's build dependencies. It follows what `docs/demo/` already does (a separate `package.json`), without importing anything from there. Playwright and `@playwright/test` are pinned to an exact version at least two weeks old (the project's dependency rule); Dependabot has an entry for `/e2e`.
2. **How the stack runs in CI: the repository's `docker compose`** (`postgres`, `mailpit`, `api`, `frontend`), built on the runner itself, with `seed-demo`. `e2e/scripts/stack.mjs up` deletes the `sportsbooking-e2e` project (its own name and volume, so it never touches a development stack), builds, waits for a request that goes through the site, the rewrite, the API and the database, and seeds. It is the same command in CI and locally. Why not "built artifacts" (`dotnet run` + `next start` on the runner): it would be a second way of starting the application, different from the images used in production (Render's `Dockerfile` and the site's) and from what the README tells people to run, and it would require installing the .NET SDK and a PostgreSQL just for this. The cost is building two images (to be measured; the target is under 8 minutes in total).
3. **Flows (Chromium only), three and no more:** (1) the player signs in, books a free time, sees it in `As minhas reservas` (My bookings) and cancels; (2) two players with the same time on screen: the second gets the 409 `slot_taken` and the message, and only one booking exists; (3) the owner signs in, sees the booking in the calendar, and a club that does not exist really answers 404. Test 2 is not a race: the real race (20 simultaneous requests) is already an integration test and [ADR 0003](0003-booking-exclusion-constraint.md) guarantees it; what the browser adds is what the player sees when they lose.
4. **Data and repeatability.** Demo accounts (`jogador01`…`jogador04`, `dono01`), always the Padel Foz Clube. Each test uses the first free day from day 20, 21 and 22 on (the seed only books up to 14 days ahead), chosen through the public API, and cancels the bookings it made at the end and at the start (only those on days ≥ 20, which no demo account has), so they can run again on the same database. Whoever is not the subject of the test signs in through the API and not through the form (limit of 10 sign-ins per minute per IP).
5. **Stability.** Locators by role and name (a `ul[aria-labelledby]` only for a day's group), waits only through `expect`/auto-waiting (never `sleep`), one worker, `retries: 1` only in CI, `trace` and `video` on the first retry, the report and videos uploaded as an artifact on failure. The configuration refuses an address that is not `localhost`.
6. **A separate workflow, `e2e.yml`** (on `pull_request` and on `push` to `main`), with `contents: read`, and not a job in `ci.yml`: `deploy.yml` only waits for `CI`, so E2E neither delays nor blocks it. Accepted consequence: since `main` has no branch protection, nothing requires E2E to be green to merge; it is checked by hand, like CI.

## Alternatives considered

- `frontend/e2e/`: closer to the site, but it mixes in the front end's dependencies and Dependabot.
- A job in `ci.yml`: a slow or unstable E2E would block the deploys.
- New accounts by registration in each test: independent of the seed, but slower and runs into the limit of 5 registrations per minute per IP; the request was to use seeded accounts.
- More flows (admin, series, password recovery): they stay in the component and integration tests; each end-to-end flow adds time and a risk of instability.

## Consequences

- One more workflow (about half the time is building the images) and a folder with its own dependencies.
- What the browser sees is now verified with the whole stack on every PR; a configuration error between the site, the rewrite and the API (the `Origin`, the cookies, the proxy) shows up here and not only in production.
- The flows depend on texts and roles of the pt-PT interface; changing the text of a button means changing the test.
