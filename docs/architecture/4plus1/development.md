# Development view

<!-- 4+1 view: development (implementation). No direct equivalent in C4. See README.md. -->

How the code is organized for whoever writes it: the repository folders, the back end projects and their references, the front end folders, what CI checks, how it is tested and how a feature is added. Everything was checked against the repository files (`.csproj`, `Directory.Packages.props`, `package.json`, `ci.yml`) on 2026-10-29, at commit `f4057ca`; the ids are in the [inventory](../inventory.md) (section 9 for the `DEV-` ones).

## 1. Repository structure

![Repository structure](../out/development-repo.svg)

Source: [development-repo.puml](development-repo.puml).

| Folder or file | Id | What it has |
| --- | --- | --- |
| `backend/` | DEV-BACKEND | The .NET solution (`SportsBooking.slnx`), `Directory.Build.props`, `Directory.Packages.props`, `dotnet-tools.json` and the API's `Dockerfile`. |
| `backend/src/` | L-DOM, L-APP, L-INF, L-API | The four application projects (section 2). |
| `backend/tests/` | DEV-TEST-UNIT, DEV-TEST-INT | The two test projects. |
| `frontend/` | DEV-FRONTEND | The Next.js application, the configuration (`next.config.ts`, `vitest.config.mts`, `eslint.config.mjs`, `tsconfig.json`, `components.json`) and the `Dockerfile`. |
| `frontend/src/` | WEB-* | `app/`, `components/`, `features/`, `lib/`, `test/` and `proxy.ts` (section 3). |
| `docs/` | DEV-DOCS | `backlog.md` and `stack.md` (sources of truth) and the architecture. |
| `docs/architecture/` | DEV-ARCHDOCS | These documents, `adr/` (the index and the written ADRs), `c4/` and `4plus1/` with the `.puml` files, `out/` with the `.svg` files, `style.puml` (the common style) and `render.sh` and `render.ps1`. |
| `scripts/` | DEV-SCRIPTS | `check-architecture-docs.sh` and `check-architecture-docs.ps1`; `generate-icons.mjs`, which generates the site's icons from `icon.svg`. |
| `.github/workflows/` | DEV-WORKFLOWS | `ci.yml`, `codeql.yml`, `e2e.yml`, `deploy.yml`, `prod-data.yml` and `keepalive.yml`. |
| `e2e/` | DEV-E2E | End-to-end tests (Playwright, Chromium) with their own `package.json`, `playwright.config.ts`, `tests/` (five flows), `support/`, `scripts/stack.mjs` (brings up and seeds the stack) and `compose.e2e.yml`. |
| `docker-compose.yml`, `.env.example` | DEV-COMPOSE | The local stack: `postgres` (image `postgres:18.6`), `mailpit` (`axllent/mailpit:v1.31.6`), `api` (built from `backend/`) and `frontend` (built from `frontend/`). |
| `global.json`, `.editorconfig`, `.gitattributes`, `.gitignore`, `README.md`, `AGENTS.md`, `LICENSE`, `SECURITY.md` | DEV-ROOTCFG | At the root. `global.json` pins SDK 10.0.400 (`rollForward: latestFeature`) and picks the `Microsoft.Testing.Platform` runner. `.editorconfig` enforces UTF-8, `lf`, 2 spaces (4 in `.cs`) and file-scoped namespaces. `.gitattributes` pins `eol=lf`. |

T06 deployment workflows: `.github/workflows/deploy.yml` and `prod-data.yml`, which use `.github/actions/install-dotnet`. See the [physical view](physical.md#48-where-migrations-and-commands-run). There is no `Caddyfile` (see the [Notes](#7-notes-and-differences-found)).

## 2. Back end

### Projects and references

![Back end projects](../out/development-backend.svg)

Source: [development-backend.puml](development-backend.puml).

The references are those of the `.csproj` files. All the projects inherit from `Directory.Build.props`: `net10.0`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `AnalysisLevel` `latest-recommended` and `ManagePackageVersionsCentrally` (all versions are in `Directory.Packages.props`).

| Project | Id | References | Main packages (version) |
| --- | --- | --- | --- |
| `SportsBooking.Domain` | L-DOM | nothing | none |
| `SportsBooking.Application` | L-APP | Domain | `FluentValidation` 12.2.0, `Microsoft.EntityFrameworkCore` 10.0.13, `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.13 |
| `SportsBooking.Infrastructure` | L-INF | Application | `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `EFCore.NamingConventions` 10.0.1, `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.13, `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` 10.0.13, `Microsoft.IdentityModel.JsonWebTokens` 8.19.2, `MailKit` 4.18.2, `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 10.0.13, `Microsoft.EntityFrameworkCore.Design` 10.0.13 (private); `FrameworkReference Microsoft.AspNetCore.App` |
| `SportsBooking.Api` | L-API | Infrastructure | `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.13, `Microsoft.AspNetCore.OpenApi` 10.0.13, `Microsoft.Extensions.ApiDescription.Server` 10.0.13 (generates the document at build time), `Swashbuckle.AspNetCore.SwaggerUI` 10.3.1, `Microsoft.EntityFrameworkCore.Design` 10.0.13 (private) |
| `SportsBooking.UnitTests` | DEV-TEST-UNIT | Domain, Api | `xunit.v3` 4.0.2, `Shouldly` 4.3.0, `Microsoft.Extensions.TimeProvider.Testing` 10.11.0 |
| `SportsBooking.IntegrationTests` | DEV-TEST-INT | Api | `xunit.v3` 4.0.2, `Shouldly` 4.3.0, `Microsoft.AspNetCore.Mvc.Testing` 10.0.13, `Testcontainers.PostgreSql` 4.16.0, `Microsoft.Extensions.Diagnostics.Testing` 10.11.0, `Microsoft.Extensions.TimeProvider.Testing` 10.11.0 |

The local tool `dotnet-ef` 10.0.13 is in `dotnet-tools.json` (`dotnet tool restore`). Two points that the diagram shows and that matter:

- **The dependency rule is checked by the compiler.** `Api` only references `Infrastructure`, which references `Application`, which references `Domain`; `Domain` has no references or packages. An Application class cannot use Npgsql because the package is not in its project (it only has `Microsoft.EntityFrameworkCore`, for `IAppDbContext`).
- **Api reaches Application and Domain only through a transitive reference** (via Infrastructure). The unit tests reference Domain and Api, and through the latter reach Application and Infrastructure; Api gives them `InternalsVisibleTo`.

### How dependency injection is composed

![Dependency injection composition](../out/development-di.svg)

Source: [development-di.puml](development-di.puml).

`Program.cs` only composes. It first registers `AddApplication()` (validators as Singleton and services as Scoped), then `AddInfrastructure(configuration)`, which internally calls `AddAccounts`, `AddSessions` and `AddEmail`, and finally what belongs to Api itself: `AddControllers`, `AddApiProblemDetails`, `AddOriginCheck`, `AddApiRateLimiting`, `AddCookieJwtAuthentication` and `AddOpenApi`. Each layer registers itself; the `DbContext` and its users are Scoped, and the `OutboxDispatcher` (`BackgroundService`) creates a scope per pass. After `Build()`, `Program.cs` assembles the pipeline (see [04](process.md)) and, **if it is not the OpenAPI generator**, validates the options (`IStartupValidator`), applies the migrations (in Development, or with `Database:ApplyMigrationsOnStartup`), does the admin seed and handles the `seed-demo` and `set-admin-password` commands (which exit without starting the server, C-CLI).

The OpenAPI generator starts the application as `GetDocument.Insider`, with no database or real configuration: that is why `Program.cs` gives it in-memory placeholder settings (`ConnectionStrings:Default`, `Security:AllowedOrigins:0`, `Jwt:SigningKey`, `Email:*`, `Outbox:Enabled`, `PublicSite:BaseUrl`) and skips the migrations and the seed. A new required setting needs a placeholder value there.

## 3. Front end

![Front end folders](../out/development-frontend.svg)

Source: [development-frontend.puml](development-frontend.puml).

| Folder | Ids | What it has |
| --- | --- | --- |
| `src/app/` | WEB-AREA-PLAYER, WEB-AREA-MANAGE, WEB-AREA-ADMIN, WEB-AREA-ACCOUNT | The routes: `(player)/` (`/`, `/clubs/[slug]`, `/bookings`), `manage/`, `admin/`, `login/`, `register/` (and `register/owner`), `forgot-password/`, `reset-password/`. They are Server Components by default; each area has `layout.tsx`, `error.tsx` and `loading.tsx`. |
| `src/features/<name>/` | WEB-F-* | One folder per feature (`admin`, `auth`, `booking`, `calendar`, `catalog`, `clubs`, `my-bookings`, `pitches`, `statistics`) with `api.ts`, `components/`, `schemas/` where there are forms, and the tests alongside. |
| `src/components/` | WEB-GUARDS, WEB-UI | Shared pieces: guards (`area-guard`, `player-guard`, `guest-only`), `providers`, `form-field`, headers and route errors; `ui/` has the five shadcn components in use (`button`, `input`, `label`, `native-select`, `sonner`). |
| `src/lib/` | WEB-API-CLIENT, WEB-SERVER, WEB-DATETIME, DEV-WEBTYPES | `api/` (client, error messages by `code`, `form-errors`, generated `schema.d.ts`), `server/` (server only), `auth/` (redirect decisions), `datetime.ts`, `guid.ts`, `utils.ts`. |
| `src/proxy.ts` | WEB-PROXY | On `/manage/*`, `/admin/*` and `/bookings`, writes the path to the `x-pathname` header; answers 404 to an impossible id in those areas and to a public club the API does not have (`/clubs/{slug}`), before the page starts sending. |
| `src/test/` | DEV-TEST-WEB | `msw.ts`, `render.tsx`, `navigation.tsx` and `server-only.ts` (test support). |

The alias `@/*` points to `src/*` (`tsconfig.json` and `vitest.config.mts`). There are 49 files with `"use client"` (not counting tests): the rest is server or shared code.

### Generated API types

![From the controller to the types](../out/development-api-types.svg)

Source: [development-api-types.puml](development-api-types.puml).

The OpenAPI document (CC-OPENAPI) is written on every Api build (`OpenApiGenerateDocumentsOnBuild` by default; the `Dockerfile` turns it off with `-p:OpenApiGenerateDocumentsOnBuild=false`) to `backend/src/SportsBooking.Api/openapi/sportsbooking-api.json`. `npm run gen:api` (`openapi-typescript` 7.13.0) turns it into `frontend/src/lib/api/schema.d.ts` (DEV-WEBTYPES), which the modules import with `import type { components } from "@/lib/api/schema"` (the `features/*/api.ts` and `types.ts`, `lib/api/client.ts` and `lib/server/*`). Both files go into the repository and CI fails if either is out of date (section 4). No API response type is written by hand.

### The `server-only` boundary

The four files in `src/lib/server/` (`session.ts`, `public-club.ts`, `admin-stats.ts`, `api-url.ts`) start with `import "server-only"`: Next.js fails the build if any of them enters a browser bundle. That folder reads the user's cookies (`session.ts`, `admin-stats.ts`) and `API_INTERNAL_URL` (the API's internal address, which the browser never sees), so it is only imported by server code: `components/area-guard.tsx`, `player-guard.tsx`, `guest-only.tsx`, `app/(player)/clubs/[slug]/page.tsx` and `app/admin/page.tsx`. The `server-only` package is not in `package.json`: Next.js provides the module at build time; in tests, `vitest.config.mts` swaps it for `src/test/server-only.ts`, so the modules load as normal modules. The browser only uses `lib/api/client.ts` (WEB-API-CLIENT), which talks to `/api/*` on the same origin.

### Main packages (exact versions from `package.json`)

| Package | Version | What for |
| --- | --- | --- |
| `next` | 16.4.2 | Framework (App Router, `cacheComponents`, `proxy.ts`, `output: "standalone"`) |
| `react`, `react-dom` | 19.3.0 | Interface |
| `typescript` | 5.9.3 | `strict` |
| `@tanstack/react-query` | 5.104.1 | API data |
| `react-hook-form`, `@hookform/resolvers`, `zod` | 7.89.0, 5.9.1, 4.6.7 | Forms and UX validation |
| `tailwindcss`, `@tailwindcss/turbopack` | 4.3.3 | Styles |
| `radix-ui`, `class-variance-authority`, `cn`, `lucide-react`, `tw-animate-css`, `shadcn` | 1.8.1, 0.7.1, 0.5.0, 1.61.0, 1.4.0, 4.22.1 | Components (shadcn/ui) |
| `recharts` | 3.10.1 | Admin charts |
| `sonner`, `next-themes` | 2.0.8, 0.4.6 | Notices and theme |
| `openapi-typescript` | 7.13.0 | API types |
| `vitest`, `jsdom`, `@testing-library/react`, `@testing-library/dom`, `@testing-library/jest-dom`, `@testing-library/user-event`, `msw` | 5.0.5, 29.1.1, 16.3.3, 10.4.2, 7.0.1, 14.6.8, 3.0.2 | Tests |
| `eslint`, `eslint-config-next` | 9.39.5, 16.4.2 | Lint |

Node 22 (`engines`, CI and Docker images). The scripts: `dev`, `build`, `start`, `lint`, `typecheck` (`next typegen && tsc --noEmit`), `test` (`vitest run`), `test:watch` and `gen:api`.

## 4. Build and quality pipeline

### CI (`ci.yml`)

![CI pipeline](../out/development-ci.svg)

Source: [development-ci.puml](development-ci.puml). The deploy and the production data are in the [next section](#deploy-production-data-and-keepalive-deployyml-prod-datayml-and-keepaliveyml).

`ci.yml` runs on every `push` and `pull_request` (X-GHA), with `permissions: contents: read` and a per-ref `concurrency` that cancels the previous run. The three jobs do not depend on each other.

| Job | Id | Steps, in order | What it catches |
| --- | --- | --- | --- |
| `backend` | DEV-CI-BACKEND | `checkout` → install the .NET SDK → `dotnet tool restore` → `dotnet restore` → `dotnet build -c Release --no-restore` → `dotnet test -c Release --no-build` → `git diff --exit-code -- src/SportsBooking.Api/openapi` → `dotnet ef migrations has-pending-model-changes` → `dotnet package list --vulnerable --include-transitive` | Warnings (they are errors), tests, outdated OpenAPI, an EF model change without a migration, vulnerable NuGet packages (direct and transitive). |
| `frontend` | DEV-CI-FRONTEND | `checkout` → `setup-node` (22, npm cache) → `npm ci` → `npm run lint` → `npm run typecheck` → `npm test` → `npm run build` → `npm run gen:api` + `git diff --exit-code -- src/lib/api` → `npm audit --omit=dev --audit-level=high` | Lint, types, tests, build, API types out of date, high vulnerabilities in production dependencies. |
| `diagrams` | DEV-CI-DIAGRAMS | `checkout` → `docs/architecture/render.sh` → `git status --porcelain -- docs/architecture/out` must be empty → `scripts/check-architecture-docs.sh` | An `.svg` that does not match the `.puml`, or was never committed; a relative link that does not reach a file. |

**Why the .NET SDK does not come from `actions/setup-dotnet`.** That action's downloads go through `aka.ms` to `dotnetorigin.blob.core.windows.net`, which answers 409 (`PublicAccessNotPermitted`; [actions/setup-dotnet#794](https://github.com/actions/setup-dotnet/issues/794)). The step downloads `dotnet-install.sh` from `builds.dotnet.microsoft.com` and installs the exact version from `global.json` (`--jsonfile global.json`, `--azure-feed https://builds.dotnet.microsoft.com/dotnet`); an exact version avoids the `aka.ms` lookup. When the problem is resolved the action can be used again (confirm that the install log no longer mentions `dotnetorigin`).

**CodeQL (`codeql.yml`, PL-CODEQL).** A separate workflow, for C# and JavaScript/TypeScript, with no build (`build-mode: none`), on every `pull_request`, on every `push` to `main` and on Mondays. It is not a job in `ci.yml`: `deploy.yml` only waits for the `CI` workflow, so a slow or failing analysis never delays a deploy.

**E2E (`e2e.yml`, PL-E2E).** Another separate workflow, on every `pull_request` and every `push` to `main`, with `contents: read`: it installs Node 22, runs `npm ci` in `e2e/`, installs Chromium (the browsers folder comes from the cache, keyed on Playwright's exact version), brings up the `docker compose` stack with the demo data (`npm run stack:up`) and runs the five tests with one worker and one retry. If it fails, it uploads the report, the traces and the videos as an artifact and shows the containers' logs. It is not a job in `ci.yml` for the same reason as CodeQL: `deploy.yml` only waits for `CI`. See [ADR 0010](../adr/0010-playwright-end-to-end-tests.md).

**The integration tests need Docker**, which the `ubuntu-latest` runners have; locally, without Docker, they all fail to connect (it looks like hundreds of failures). `main` **has no branch protection** on GitHub (verified on 2026-10-30: the API answers "Branch not protected"), so nothing requires CI or E2E to be green: it is checked by hand, before each merge, that the three CI jobs and the E2E workflow passed.

### Deploy, production data and keepalive (`deploy.yml`, `prod-data.yml` and `keepalive.yml`)

![Deploy, production data and keepalive pipelines](../out/development-deploy.svg)

Source: [development-deploy.puml](development-deploy.puml).

`deploy.yml` (PL-DEPLOY) runs after CI finishes successfully on a `push` to `main`, only with the `DEPLOY_ENABLED` variable set to `true`, and has four jobs in a row: `migrate` (PL-MIGBUNDLE, as schema owner), `deploy-api` (PL-DEPLOYHOOK, and waits for 200 on `/health`), `deploy-web` (Vercel CLI) and `smoke`. `prod-data.yml` (PL-PRODDATA) is manual and runs `migrate-only`, `set-admin-password` or `seed-demo` against the production database. The two share the `deploy` `concurrency` group, without cancelling the one that is running. `keepalive.yml` (PL-KEEPALIVE) runs on Mondays and Thursdays and by hand, only with `DEPLOY_ENABLED` set to `true`, and makes a `GET /api/localities` through the public site, with retries for about 5 minutes: it is a read query against the database, which also wakes the API, so that Supabase does not pause the project for inactivity. It has its own `concurrency` group and uses no secrets, only the `SITE_URL` variable (see the [runbook, section 10](../../deployment/runbook.md#10-keep-the-demo-alive)). The manual steps and the secrets are in the [runbook](../../deployment/runbook.md) and in [environment-variables.md](../../deployment/environment-variables.md); the decision, in [ADR 0009](../adr/0009-deployment-target.md).

**Locally.** What CI does is repeated with `dotnet build backend/SportsBooking.slnx`, `dotnet test backend/SportsBooking.slnx` and, in `frontend/`, `npm run lint`, `npm run typecheck`, `npm test`, `npm run build`, `npm run gen:api`; the diagrams with `docs/architecture/render.sh` (or `.ps1`), which only needs Docker.

## 5. Testing strategy

Two automatic layers on the back end, one on the front end, five end-to-end tests with the complete stack (T08, US30, T09) and manual verification in the browser for the rest. The practice, before merging a story with an interface, is to walk through by hand (or with temporary browser scripts, which are not kept) the flows that the end-to-end tests do not cover, with the complete stack (`docker compose`, demo data and Mailpit), and to check the emails and the logs.

| Layer | Id | Tools | What it covers | What it does not cover |
| --- | --- | --- | --- | --- |
| Back end unit | DEV-TEST-UNIT | xUnit v3, Shouldly, `FakeTimeProvider` | Domain and policy rules (slots and clock changes, cancellation window, passwords), validators, services without a database, email composition, Api security pieces (access through `InternalsVisibleTo`) | Anything that depends on SQL, constraints or transactions. |
| Back end integration | DEV-TEST-INT | xUnit v3, `WebApplicationFactory<Program>`, Testcontainers PostgreSQL (`postgres:18.6`), Shouldly | Complete endpoints from authentication to the database: authorization by profile and by owner, `bookings_no_overlap` and simultaneous requests, error codes, outbox and worker, startup and seed, request limits | The interface. EF Core's in-memory provider is never used. |
| Front end components | DEV-TEST-WEB | Vitest 5.0.5 (jsdom), Testing Library, MSW 3.0.2 | UI behaviour with the API simulated: per-field errors, messages by `code`, URL as state, calendar layout, pure functions (`datetime`, `redirects`, `selection`) | The real API, the real browser, the httpOnly cookies. |
| End-to-end | DEV-E2E | Playwright 1.64.1 (Chromium), `docker compose` with the demo data | Five flows through the site, the rewrite, the API and the database: the player books and cancels; the second of two players gets the 409 `slot_taken`; the owner sees the booking in the calendar and a non-existent club gives 404; a new account exports its data, deletes itself and can no longer sign in; a player switches the site to English, reloads and books. | Everything else (admin, series, password recovery, emails), which stays in the layers above and in manual verification. |
| Manual verification | — | Browser, `docker compose`, Mailpit | The flows that end-to-end does not cover, before the merge. | Not repeated automatically. |

**Counts** (2026-10-30, `f4057ca` with the T06 pre-deploy hardening pull request, `Release`, verified by running the tests):

| Set | Cases run | Test methods | Notes |
| --- | --- | --- | --- |
| Back end unit | 549 | 291 in 39 classes | `dotnet test --project backend/tests/SportsBooking.UnitTests`; `[Theory]`s count once per data row. |
| Back end integration | 831 | 593 in 49 classes | About 2 min 40 s, with Docker. |
| Front end | 995 | — | 64 test files; about 100 s. |

The "methods" come from `dotnet <SportsBooking.*Tests.dll> -list methods`; the "cases" from the summary of `dotnet test` and `vitest run`.

### What the integration tests are like

- **One PostgreSQL per run, one database per test that needs it.** `ApiFactory` (`AssemblyFixture`, `Testing` environment) starts a `postgres:18.6` container and applies the migrations at startup (`Database:ApplyMigrationsOnStartup=true`, as in Development). The tests share that database and give each test unique localities or names; those that depend on startup or on totals use `factory.CreateIsolatedApp(name, settings)`, with their own database and captured logs.
- **Data with `TestVenues`** (owners, clubs, pitches, blocks, bookings) through the domain model, and sessions with `CreateSignedInAsync`, without the cost of PBKDF2.
- **Origin, network and clock.** The site origin is `https://localhost` (for the `Secure` cookies); `TestClientAddress` fixes the connection IP; very high request limits on the shared host (tested in isolated apps); `FakeTimeProvider` (or `SettableTimeProvider`) where time matters, knowing that the access token is validated against the system clock. The outbox is off (`Outbox:Enabled=false`) and the worker tests run the processor by hand with `FakeEmailSender`; `MailpitTests` uses a real Mailpit.
- **Connection pool limits.** Each test host has its own Npgsql pool, and PostgreSQL has a connection limit for the whole suite. Without care, a few dozen hosts (some already disposed, with idle connections) exhausted the connections and made unrelated tests fail. That is why: the container starts with `max_connections=400` (the default is 100); the isolated apps use `Maximum Pool Size=8;Minimum Pool Size=0;Connection Idle Lifetime=15`; `AdminTestApp` uses `Maximum Pool Size=4` and a semaphore that lets at most 4 hosts run at the same time; `PasswordResetTests` and `BookingNotificationTests` use 6; the test with 20 requests waiting on the pitch lock (`RecurringBookingTests`) uses 8, so as not to take twenty connections from the server. Tests that create their own pool call `NpgsqlConnection.ClearPool` when they finish.

## 6. Conventions

**Git.** The rule is that `main` only receives pull requests with green CI, with **squash merge**; it is not enforced by branch protection, and green CI is checked by hand before each merge. One branch per story (`feat/US18-short-name`, `chore/T01-...` for the technical ones, `docs/...`, `ci/...`), branching from `main`. Commits follow Conventional Commits with the story id: `feat(US18): ...`, `test(T03): ...`, `fix(US19): ...`, `docs(architecture): ...`. Each bug first gets a test that reproduces it, and only then the fix. `main`'s history has one commit per story (squash); the first five pull requests (#1 to #5) were left with merge commits.

**Code and text.** Code, identifiers, commits and branches in English; the interface is available in Portuguese (pt-PT, the default) and English, and API error messages are in pt-PT. The architecture documents are in English, with the code's names kept as they are.

**ADRs.** Important decisions go in `docs/architecture/adr/` (`NNNN-short-title.md`; an accepted ADR is not rewritten, it is replaced by another). The [index](../adr/README.md) has the status of each one; all twelve ADRs (0001 to 0012) are accepted, including 0009 (deployment target).

**Migrations.** Generated with `dotnet ef migrations add <Name> --project src/SportsBooking.Infrastructure --startup-project src/SportsBooking.Api --output-dir Persistence/Migrations`, from `backend/`. An already applied migration is never edited; another is added (there are 21). The folder is marked `generated_code`. Hand-written SQL (`bookings_no_overlap`, partial indexes) goes into the migration itself.

**Definition of done.** A story is only done when each acceptance criterion is covered by tests (unit, integration and/or component) and CI is green.

### How a feature is added

The order follows the dependencies: each step builds on the previous one. The squash commits of a story (for example `eb94b07`, US20) touch all of these places.

| # | Step | Where | Verification |
| --- | --- | --- | --- |
| 1 | Rule in `docs/backlog.md` (criteria) and, if it is a technical decision, in `docs/stack.md` | DEV-DOCS | — |
| 2 | **Entity and migration**: intention-revealing method on the entity (private setters), EF configuration, `dotnet ef migrations add` | L-DOM, L-INF (`Persistence/Migrations/`) | `has-pending-model-changes` empty |
| 3 | **Service** with `Result<T>`, FluentValidation validator and stable error codes; the interface (`I*Store`) in Application and the implementation in Infrastructure; registration in `AddApplication()` / `AddInfrastructure()` | L-APP, L-INF | unit tests |
| 4 | **Endpoint**: thin controller under the profile's policy (on owner routes, `api/manage/clubs/{clubId}/…` and `OwnedClubs`) | L-API | — |
| 5 | **Tests**: unit for the rule, integration for the endpoint (authorization, errors, concurrency); new routes in the access tests' lists (`OwnerAreaAccessTests`, `AdminAreaAccessTests`) | DEV-TEST-UNIT, DEV-TEST-INT | `dotnet test` |
| 6 | **OpenAPI and types**: `dotnet build` regenerates the document; `npm run gen:api` regenerates `schema.d.ts`; commit both | CC-OPENAPI, DEV-WEBTYPES | empty `git diff` in CI |
| 7 | **Front end**: the feature's `api.ts`, components, Zod schema that mirrors the API's limits, messages by `code` in `error-messages.ts`, state in the address, component tests with MSW | WEB-F-*, WEB-API-CLIENT | `lint`, `typecheck`, `test`, `build`; manual verification in the browser |
| 8 | **Documentation**: backlog criteria, `AGENTS.md` if the rule changes the code map, and, if it touches an inventory element, the inventory and the diagrams (and `docs/architecture/render.sh`) | DEV-DOCS, DEV-ARCHDOCS | `diagrams` job |

## 7. Notes and differences found

Differences between the code and other documents, only recorded here (this part changes no code, tests or other documents):

1. `docs/stack.md` (section "Repository, Git, CI and deploy") shows a `Caddyfile` in the repository tree, which does not exist: [ADR 0009](../adr/0009-deployment-target.md) replaces that part. The same section promises ADRs "in `docs/`": they are in `docs/architecture/adr/` (0001 to 0012).
2. The root `README.md` was rewritten (pull request #29) and now describes the three CI jobs, including `diagrams`, and the current repository structure; this note stays only as a record.
3. `frontend/Dockerfile` copies `/app/public`, which does not exist in the repository; the `Dockerfile` itself creates the empty folder (`mkdir -p public`) and explains it in a comment. It is not an error.
4. `main` has no branch protection on GitHub; `README.md`, `docs/stack.md` and `AGENTS.md` say so as it is, and green CI is checked by hand before each merge.
