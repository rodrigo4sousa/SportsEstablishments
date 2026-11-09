# Sports pitch booking

A web application for booking sports pitches and courts (futsal, 7-a-side football, padel, tennis) at clubs in Portugal. Players search clubs and book free slots; owners run their clubs, pitches, schedules and bookings; the platform admin follows the platform through aggregate numbers. It is one Next.js app with three areas on top of an ASP.NET Core API and PostgreSQL. The user interface is in Portuguese (pt-PT); the code and this README are in English.

[![CI](https://github.com/rodrigo4sousa/SportsEstablishments/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/rodrigo4sousa/SportsEstablishments/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Next.js 16](https://img.shields.io/badge/Next.js-16-000000?logo=nextdotjs&logoColor=white)
![TypeScript](https://img.shields.io/badge/TypeScript-strict-3178C6?logo=typescript&logoColor=white)
![PostgreSQL 18](https://img.shields.io/badge/PostgreSQL-18-4169E1?logo=postgresql&logoColor=white)
![Docker Compose](https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker&logoColor=white)

## Contents

- [Overview](#overview)
- [Features by role](#features-by-role)
- [Architecture](#architecture)
- [Technology and why](#technology-and-why)
- [Engineering highlights](#engineering-highlights)
- [Getting started](#getting-started)
- [Running the tests and checks](#running-the-tests-and-checks)
- [Project structure](#project-structure)
- [Documentation](#documentation)
- [Status and roadmap](#status-and-roadmap)
- [Development workflow](#development-workflow)
- [Author and contact](#author-and-contact)
- [License](#license)

## Overview

The platform is a single marketplace: one player account works at every club of every owner, and there are no per-club sites. It has three roles:

- **Player** (`/`): searches clubs, sees free slots, books, repeats a booking weekly and cancels.
- **Owner** (`/manage`): owns one or more clubs and manages pitches, opening hours, prices, blocks, the booking calendar and bookings for customers without an account.
- **Admin** (`/admin`): the platform owner. The account is created only from configuration at startup, never through an endpoint. The admin sees counts, never a player's name, email or phone.

Without an account, a visitor can search clubs by sport, locality and date, open a club page and see each pitch's free slots and prices. Booking needs a player account; after signing in or registering, the visitor returns to the same pitch, date and slot.

<p>
  <img src="docs/images/screenshot-club-search-results.png" alt="Club search with sport and date filters and a list of clubs with free slots" width="49%">
  <img src="docs/images/screenshot-club-page-slots-and-prices.png" alt="Club page with pitches, base price and the free slots of one pitch with their prices" width="49%">
</p>
<p>
  <img src="docs/images/screenshot-owner-calendar-day.png" alt="Owner calendar, day view with one column per pitch, bookings, a block and a booking created by the club" width="49%">
  <img src="docs/images/screenshot-admin-top-occupation-registrations.png" alt="Admin dashboard: registrations per week and the ten clubs with the highest occupation" width="49%">
</p>

Search, club page, owner calendar and admin dashboard. All eight screenshots, and how they were made, are in [docs/images](docs/images/README.md).

## Features by role

**Player**

- Search clubs by sport, locality and date; a club page lists its pitches with each slot's availability and price.
- Book a free slot from today up to 30 days ahead. The price is fixed at booking time.
- Repeat a booking weekly for 2 to 4 weeks, with a preview of every date, its price and why a date cannot be booked.
- See upcoming and past bookings and cancel within the club's cancellation window (24 hours by default, set per club; the window in force at the moment of cancelling applies).
- Emails: confirmation, cancellation, a reminder 3 hours before the start, a summary for a weekly series, and password reset.

**Owner**

- Clubs (several per owner) and pitches of four sports. Pitches are deactivated, never deleted, and cannot be deactivated while confirmed bookings have not ended.
- Weekly opening hours and a slot length of 30, 60 or 90 minutes per pitch. Slots are computed from the schedule, never stored.
- Price bands by weekday and time of day; a slot outside every band uses the pitch's base price.
- Blocks for maintenance or private events, refused when they overlap confirmed bookings.
- A daily and weekly booking calendar, booking details, and cancellation with a required reason, which is emailed to the player.
- Bookings for customers who phone or walk in, without an account, under the same double-booking rules.
- No-show tracking: mark and undo a no-show on a booking that has ended, and see the player's no-show count across the owner's clubs.
- An optional email per new booking (a switch per club).
- Statistics per club and per pitch: booked and open hours, occupation, revenue and bookings for a chosen period.

**Admin**

- Platform totals, bookings over time (by day, week or month), registrations over time, the 10 clubs with the highest occupation, and a searchable, sortable list of owners.
- Deactivate and reactivate an owner with a required reason. A deactivated owner cannot sign in or refresh a session, and their clubs disappear from the public pages; existing bookings stay confirmed. Every change is recorded in an append-only history that a database trigger protects from updates and deletes.

## Architecture

- **One Next.js app with three areas** (player, owner, admin). Server Components render first views; Client Components handle interaction. Next.js also rewrites `/api/*` to the API, so the browser sees a single origin.
- **An ASP.NET Core Web API** in four projects (Clean Architecture: Api → Infrastructure → Application → Domain). It owns every business rule.
- **PostgreSQL**, the only data store, also enforces the rule against double bookings.
- **A background email worker** inside the API process sends messages written to an outbox table in the same transaction as the change that caused them.

<img src="docs/architecture/out/01-context.svg" alt="C4 system context: visitor, player, owner and admin use the booking system, which sends email over SMTP; Vercel and Render host it and Brevo sends the email in production" width="800">

<img src="docs/architecture/out/02-containers-dev.svg" alt="C4 containers in development: browser, Next.js, ASP.NET Core API with the email worker, PostgreSQL and Mailpit" width="500">

System context (top) and containers in development (bottom). Diagram labels are in Portuguese. The [architecture README](docs/architecture/README.md) is the entry point to all views, with C4 levels 1 to 4, the 4+1 views (logical, process, development, physical and scenarios), an inventory of elements mapped to the code, and a gallery of all 40 diagrams.

## Technology and why

Versions are those in [frontend/package.json](frontend/package.json), [backend/Directory.Packages.props](backend/Directory.Packages.props), [global.json](global.json) and [docker-compose.yml](docker-compose.yml).

| Layer | Technology | Why |
| --- | --- | --- |
| Web app | Next.js 16.4 (App Router), React 19.3, TypeScript 5.9 (strict) | Server Components read data on the server with the user's cookie; Client Components only where there is interaction |
| UI | Tailwind CSS 4.3, shadcn/ui | Accessible components whose code lives in the repository |
| Server data in the browser | TanStack Query 5 | Caching, refetching and invalidation after booking or cancelling |
| UI state | The page URL (`useSearchParams`) | The booking choice, calendar view and filters survive a redirect to sign-in and can be shared; no client store |
| Forms | React Hook Form 7, Zod 4 | Per-field messages in the browser; the API validates again |
| API types | openapi-typescript 7, generated from the API's OpenAPI document | The C# DTOs are the single source of truth |
| Charts | Recharts 3 | Admin charts |
| API | ASP.NET Core 10 Web API with controllers (.NET SDK 10.0.400) | Long-term support release; controllers stay thin |
| Accounts | ASP.NET Core Identity (core only) | Password hashing, lockout and reset tokens without custom crypto |
| Sessions | Short JWT plus rotating refresh token, both in httpOnly cookies | Tokens are out of reach of page JavaScript |
| Validation | FluentValidation 12 | Request rules outside controllers, unit-testable |
| Data access | EF Core 10 with Npgsql 10 | Migrations and LINQ; raw SQL only where PostgreSQL features are needed |
| Database | PostgreSQL 18 | Exclusion constraints, row locks and `SKIP LOCKED` |
| Background work | `BackgroundService` and an outbox table | Emails that neither block requests nor get lost |
| Email | SMTP through MailKit 4; Mailpit in development | No real email leaves a development machine |
| Tests | xUnit v3, Shouldly, Testcontainers, Vitest 5, React Testing Library, MSW 3 | Integration tests on real PostgreSQL; components tested against a mocked API |
| Tooling | Docker Compose, GitHub Actions, PlantUML (C4-PlantUML) | One command to run; diagrams as text, rendered and checked in CI |

## Engineering highlights

**No double bookings.** Two confirmed bookings of the same pitch cannot overlap: a PostgreSQL exclusion constraint (`bookings_no_overlap`, `btree_gist` over `tstzrange(starts_at, ends_at)`, only for confirmed bookings) decides, and its violation becomes a 409 `slot_taken`, never a 500. Bookings and blocks live in different tables, so both take a row lock on the pitch (`SELECT … FOR UPDATE`) before checking each other. An integration test sends 20 simultaneous requests for one slot and expects exactly one 201 and 19 × 409. See [ADR 0003](docs/architecture/adr/0003-exclusion-constraint-reservas.md), the [migration](backend/src/SportsBooking.Infrastructure/Persistence/Migrations/20261024134414_AddBookings.cs), [PitchLock](backend/src/SportsBooking.Infrastructure/Pitches/PitchLock.cs) and [BookingTests](backend/tests/SportsBooking.IntegrationTests/Bookings/BookingTests.cs).

**Slots that are right on clock-change days.** Opening hours are stored as local times with the club's time zone (`Europe/Lisbon`); instants are stored in UTC. Slots are blocks of real elapsed time from opening, so the day the clocks go forward has one slot fewer and the day they go back has one more, and every slot lasts exactly its length. A local time that does not exist moves past the gap; a repeated one uses its first occurrence; the UI labels the repeated hour «(verão)» or «(inverno)». All conversions go through [ClubTime](backend/src/SportsBooking.Domain/Scheduling/ClubTime.cs) and all slots come from [SlotGenerator](backend/src/SportsBooking.Domain/Scheduling/SlotGenerator.cs), with unit tests for 29 March and 25 October 2026. See [ADR 0007](docs/architecture/adr/0007-slots-calculados.md).

**Cookie sessions.** A 15-minute JWT and a 7-day refresh token travel only in httpOnly, Secure cookies; the browser learns who is signed in from `GET /api/auth/me`. Each refresh rotates the token with a conditional update, a reused refresh token revokes every session of the account, and a refresh for a deactivated account fails. Mutating requests must carry an allowed `Origin`, and login, registration, refresh and password reset are rate limited per client IP. See [ADR 0002](docs/architecture/adr/0002-sessao-em-cookies-httponly.md), [SessionService](backend/src/SportsBooking.Infrastructure/Auth/SessionService.cs) and [AuthCookies](backend/src/SportsBooking.Api/Auth/AuthCookies.cs).

**Security headers.** Every page of the web app is served with a `Content-Security-Policy` that allows only its own origin, `X-Content-Type-Options`, `Referrer-Policy`, a restrictive `Permissions-Policy` and no framing; the password-reset page keeps `no-referrer` and `no-store`. Every API response carries `nosniff` and no `Server` header. The policy has one known relaxation: it allows inline scripts and styles, because Next.js streams inline data scripts and `cacheComponents` rules out a per-request nonce (a nonce and a build-time hash were both tried in a browser and stopped hydration). HSTS exists as a build setting and stays off until the site is served over HTTPS. See [security-headers.ts](frontend/src/lib/security-headers.ts) and the T06 notes in the [backlog](docs/backlog.md).

**Per-owner isolation: 404, not 403.** Every owner query starts from the signed-in owner's clubs ([OwnedClubs](backend/src/SportsBooking.Application/Clubs/OwnedClubs.cs)), so another owner's club, pitch or booking answers the same 404 as one that does not exist; a wrong profile gets 403. [OwnerAreaAccessTests](backend/tests/SportsBooking.IntegrationTests/Clubs/OwnerAreaAccessTests.cs) discovers every `/api/manage` route and checks 401, 403 and the cross-owner 404, so new endpoints are covered automatically.

**Outbox with at-least-once delivery.** Emails are written as outbox rows in the same transaction as the booking or cancellation. The worker claims due rows with `FOR UPDATE SKIP LOCKED`, so several API instances never send the same batch at once; failures are retried with a doubling back-off, and reminders whose booking was cancelled are skipped rather than failed. See [ADR 0004](docs/architecture/adr/0004-outbox-para-emails.md) and [OutboxProcessor](backend/src/SportsBooking.Infrastructure/Notifications/OutboxProcessor.cs).

**Privacy rules backed by tests.** The admin endpoints return counts only, and every one has a test that scans the response for a seeded player's name, email and id. Owners see only a player's name, and public endpoints carry nothing about players or owners. Logs never contain passwords, tokens, reset links or email addresses; the tests check this where the risk exists.

**Tests on real PostgreSQL.** Integration tests start PostgreSQL 18 with Testcontainers and exercise endpoints from authentication to the database, including constraints, locks and time zones; the EF Core in-memory provider is never used. Time comes from an injected `TimeProvider`, so tests run on fixed clocks. The frontend has component tests with Vitest, React Testing Library and MSW. There is no end-to-end suite: user interface changes are checked by hand in the browser against the full stack.

**CI with drift checks.** Besides building and testing, CI fails if the committed OpenAPI document, the generated TypeScript API types or the rendered diagram SVGs differ from what the code and `.puml` sources produce, if the EF Core model has changes without a migration, or if production dependencies have known vulnerabilities. See [.github/workflows/ci.yml](.github/workflows/ci.yml).

## Getting started

**Prerequisites:** Docker with Compose v2. To run the API or the web app outside Docker: the .NET SDK 10 (the version in [global.json](global.json)) and Node.js 22 or later.

```bash
cp .env.example .env        # optional: local-only defaults; set the admin account here
docker compose up --build
```

| Service | URL |
| --- | --- |
| Web app | http://localhost:3000 |
| API | http://localhost:5080 (health check: `/health`) |
| API docs (Swagger UI, Development only) | http://localhost:5080/swagger |
| Mailpit (development inbox) | http://localhost:8025 |
| PostgreSQL | `localhost:5432` (user, password and database `sportsbooking`) |

In Development the API applies the EF Core migrations at startup. The session cookies are `Secure`; Chrome, Edge and Firefox accept them on `http://localhost`.

### Demo data

`seed-demo` fills an empty development database with 5 owners, 10 clubs and 43 pitches around Porto and Lisboa (all four sports), 60 players, a few blocks, and thousands of bookings from the last 60 days and the next 14, including cancellations, no-shows and customer bookings. It does not create price bands or weekly series. It is deterministic: the same day gives the same data.

```bash
docker compose run --rm api seed-demo                          # with the compose stack
dotnet run --project backend/src/SportsBooking.Api -- seed-demo   # API outside Docker (needs: docker compose up -d postgres)
```

It exits without starting the server, sends no email, and refuses when any owner or player already exists. It also refuses in Production unless `DemoData__AllowInProduction=true` is set on that one process. Every demo account has the same **public, development-only** password, `demo-password-1`: anyone can read it here, so never use it for a real account.

| Role | Email |
| --- | --- |
| Owners | `dono01@example.test` … `dono05@example.test` |
| Players | `jogador01@example.test` … `jogador60@example.test` |

**Reset the database:** `docker compose down -v` deletes the database volume; the next `docker compose up` recreates the schema, and `seed-demo` can run again.

### Admin account

There is no public sign-up for the admin. At startup the API creates the admin account from two settings, if no admin exists yet:

| Setting | Docker Compose (`.env`) | Environment variable | `dotnet run` |
| --- | --- | --- | --- |
| Email | `ADMIN_SEED_EMAIL` | `AdminSeed__Email` | `dotnet user-secrets --project backend/src/SportsBooking.Api set "AdminSeed:Email" "…"` |
| Password (8+ characters) | `ADMIN_SEED_PASSWORD` | `AdminSeed__Password` | `dotnet user-secrets --project backend/src/SportsBooking.Api set "AdminSeed:Password" "…"` |

With neither set, the API starts without an admin and logs a warning. With only one set, a malformed email or a short password, the API refuses to start; the error names the setting, never its value. The seed never changes an existing admin, never creates a second one and never promotes a player or owner account.

### Changing the admin password

The `set-admin-password` command changes the admin's password, ends all of the admin's sessions and exits without starting the server. The new password comes only from the `ADMIN_NEW_PASSWORD` environment variable, never from the command line, and is never printed or logged. Exit code 0 means it changed; 1 means it refused and changed nothing.

```bash
read -rs ADMIN_NEW_PASSWORD && export ADMIN_NEW_PASSWORD
docker compose run --rm -e ADMIN_NEW_PASSWORD api set-admin-password
unset ADMIN_NEW_PASSWORD
```

In PowerShell: `$env:ADMIN_NEW_PASSWORD = Read-Host -MaskInput "New password"`, the same `docker compose run` command, then `Remove-Item Env:ADMIN_NEW_PASSWORD`. Outside Docker: `dotnet run --project backend/src/SportsBooking.Api -- set-admin-password`.

### Running without Docker for the app

```bash
docker compose up -d postgres mailpit
dotnet run --project backend/src/SportsBooking.Api   # API on http://localhost:5080
cd frontend && npm install && npm run dev            # web app on http://localhost:3000, /api/* rewritten to :5080
```

Settings outside Development (JWT signing key, allowed origins, public site address, SMTP, database roles, trusted proxies) are listed in the [physical view, section 4.7](docs/architecture/4plus1/physical.md#47-segredos-e-variáveis-de-ambiente-por-serviço). The API refuses to start when a required setting is missing or invalid. The web app sends HSTS only if its image is built with `--build-arg ENABLE_HSTS=true`, for a site served over HTTPS (off by default). No proxy is trusted for `X-Forwarded-For` today.

## Running the tests and checks

```bash
# Backend: build (warnings are errors), then unit and integration tests. Docker must be running (Testcontainers).
dotnet build backend/SportsBooking.slnx
dotnet test backend/SportsBooking.slnx

# Frontend (in frontend/)
npm ci
npm run lint
npm run typecheck
npm test
npm run build

# Architecture docs: render all diagrams with the pinned PlantUML image (Docker), then check links and the gallery
docs/architecture/render.sh            # or docs/architecture/render.ps1
scripts/check-architecture-docs.sh    # or scripts/check-architecture-docs.ps1
```

`dotnet build` also regenerates the OpenAPI document (`backend/src/SportsBooking.Api/openapi/sportsbooking-api.json`); after it, `npm run gen:api` in `frontend/` regenerates `src/lib/api/schema.d.ts`. Commit both. A new EF Core migration is added from `backend/` with `dotnet ef migrations add <Name> --project src/SportsBooking.Infrastructure --startup-project src/SportsBooking.Api --output-dir Persistence/Migrations`.

[CI](.github/workflows/ci.yml) runs three jobs on every push and pull request:

| Job | Steps |
| --- | --- |
| Backend (build + tests) | Build in Release with warnings as errors → unit and integration tests → OpenAPI document drift → no EF Core model changes without a migration → no vulnerable NuGet packages |
| Frontend (lint, typecheck, tests, build) | Lint → typecheck → tests → build → generated API types drift → `npm audit` of production dependencies |
| Docs (architecture diagrams) | Render every diagram → fail if an SVG differs from the committed one → `check-architecture-docs.sh` |

## Project structure

```text
backend/
  src/SportsBooking.Domain/           entities, value objects and business rules; no dependencies
  src/SportsBooking.Application/      use cases (services), DTOs, validators, interfaces
  src/SportsBooking.Infrastructure/   EF Core and migrations, Identity, sessions, outbox and email, demo data
  src/SportsBooking.Api/              controllers, auth cookies, Origin check, rate limiting, ProblemDetails, DI composition
  tests/SportsBooking.UnitTests/      domain and service rules
  tests/SportsBooking.IntegrationTests/  endpoints against PostgreSQL in Testcontainers
frontend/
  src/app/                            routes: (player), manage, admin, login, register, password pages
  src/features/                       one folder per feature: components, schemas, API calls, tests
  src/lib/                            API client and generated types, server-only helpers, Lisbon date formatting
  src/components/                     shared components and shadcn/ui
docs/
  backlog.md  stack.md                stories and rules; stack and practices
  architecture/                       C4 and 4+1 views, ADRs, diagrams (.puml sources and rendered .svg)
  images/                             screenshots of the app with demo data
scripts/                              check the links and the diagram gallery of the docs
.github/workflows/ci.yml              continuous integration
docker-compose.yml                    local stack: PostgreSQL, Mailpit, API, web app
```

## Documentation

The backlog, the Stack document and the architecture documents are in Portuguese.

- [Backlog](docs/backlog.md): epics, user and technical stories with acceptance criteria, story-point estimates, business rules and the decisions taken on each story.
- [Stack and practices](docs/stack.md): the stack, backend architecture, authentication, frontend, data and concurrency, validation and errors, tests, and the security checklist.
- [Architecture](docs/architecture/README.md): reading guide, C4 and 4+1 views, element inventory and diagram gallery.
- [Architecture decision records](docs/architecture/adr/README.md): all nine are accepted; [ADR 0009](docs/architecture/adr/0009-alvo-de-implantacao.md) (deployment target) was accepted on 2026-11-09, after the first real deployment.
- Deployment: [runbook](docs/deployment/runbook.md), [environment variables](docs/deployment/environment-variables.md) and [database roles](docs/deployment/supabase-roles.sql).
- [Screenshots](docs/images/README.md): what each image shows and which data it uses.

## Status and roadmap

**Done.** The backlog counts 35 stories and 129 story points: 25 MVP stories (92 points) and 10 optional stories (37 points). Everything is built and deployed, including all optional stories: 35 stories and 129 points are complete. T06, continuous deployment (5 points), went live on 2026-11-09. Dependabot and the security headers, planned before T06, are in place.

**Deployed.**

- Deployment (T06), live since 2026-11-09 (ADR 0009, accepted): the site on Vercel, the API on Render's free plan, Supabase for PostgreSQL, Brevo for email, and GitHub Actions for the pipelines. See the [runbook](docs/deployment/runbook.md) and the [environment variables](docs/deployment/environment-variables.md).

> **Demo online:** <https://sports-establishments.vercel.app/>. It is a demo that may be taken offline at any time. The API runs on a free plan that sleeps when idle, so the first request can take about a minute. The demo accounts share a public password (see [Demo data](#demo-data)): never use it for a real account.

## Development workflow

- One branch per story, named after its ID (`feat/US18-…`, `chore/T01-…`, `docs/…`), merged into `main` through a pull request with squash merge.
- Conventional Commits with the story ID, for example `feat(US18): …` or `fix(US19): …`.
- `main` has no branch protection rule; the CI result is checked by hand before every merge.
- Dependabot proposes updates weekly: one grouped pull request per ecosystem (NuGet, npm, GitHub Actions) for minor and patch updates, with a 14-day cooldown; major updates arrive one by one and are reviewed by hand.
- Every bug gets a reproducing test before the fix.
- Significant decisions are recorded as ADRs in [docs/architecture/adr](docs/architecture/adr/README.md).

## Author and contact

> **Placeholder:** author name and public links to be added.

## License

> **Note:** the repository has no LICENSE file yet, so no license is granted.
