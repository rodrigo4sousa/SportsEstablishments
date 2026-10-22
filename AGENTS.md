# AGENTS.md

## Product
Portfolio web app for booking sports pitches/courts (futsal, futebol 7, padel, ténis) in Portuguese clubs. One Next.js app with three areas on top of one ASP.NET Core API:
- **Jogador** (`/`): searches clubs, sees free slots, books and cancels.
- **Dono** (`/manage`): owns one or more clubs; manages pitches, opening hours, blocks, calendar, bookings for walk-in customers.
- **Admin** (`/admin`): platform owner. Created only by seed (env vars), never by an endpoint. Sees counts only, never player names/emails.

Source of truth: [docs/backlog.md](docs/backlog.md) (stories, acceptance criteria, rules) and [docs/stack.md](docs/stack.md) (architecture, stack, practices). Both are in Portuguese. Don't change stack, architecture or business rules without asking. Where they disagree, the Stack doc wins for technical choices (e.g. Next.js, not a plain React app).

## Architecture rules (detail in docs/stack.md)
- Backend: Clean Architecture in `backend/src/SportsBooking.{Domain,Application,Infrastructure,Api}`. Dependencies point inward only: Api → Infrastructure → Application → Domain.
- DI: built-in container, constructor injection only (no `IServiceProvider` in business code). Each layer registers itself: `services.AddApplication()`, `services.AddInfrastructure(config)`; `Program.cs` only composes. DbContext and its users are Scoped; a `BackgroundService` creates scopes via `IServiceScopeFactory`.
- Current time comes from injected `TimeProvider`, never `DateTime.UtcNow`/`Now`. Tests use `FakeTimeProvider`.
- Options pattern with `ValidateOnStart`.
- No generic repository (Application uses `IAppDbContext`), no MediatR, no AutoMapper, no FluentAssertions 8 (use Shouldly).
- Thin controllers. Entities have private setters and intention methods (`booking.Cancel(reason, now)`). Expected failures are returned results, not exceptions. `async` + `CancellationToken` all the way.
- Public IDs: `Guid.CreateVersion7()`. Money: `decimal` / `numeric(10,2)`. Dates: UTC `timestamptz`; opening hours as `TimeOnly` + club time zone `Europe/Lisbon`.
- Overlapping bookings are prevented by a PostgreSQL exclusion constraint (`23P01` → 409), not only app code.
- Errors: ProblemDetails (RFC 9457) via `AddProblemDetails()` + global `IExceptionHandler`. Every error has a stable `code` (e.g. `slot_taken`) and `traceId`; the front end picks the message by `code`. Status codes: see table in docs/stack.md.
- Auth: ASP.NET Core Identity (core only). JWT access token (15 min) + rotating refresh token (7 days), both **httpOnly cookies**; front end learns the user via `GET /api/auth/me`. Front end and API are **same origin** (Next.js rewrites `/api/*` in dev, Caddy in prod); no CORS. API checks `Origin` on mutating requests.
- Frontend: Next.js App Router, TypeScript strict, Tailwind + shadcn/ui. Server Components by default; `'use client'` as low as possible. Server code that reads cookies or the internal API URL imports `server-only`. API types generated from OpenAPI (openapi-typescript), never hand-written.
- State: **TanStack Query** for anything from the API (invalidate after mutations, refetch slots on 409). **Zustand** only for shared UI state (booking step, calendar view); never copy API data or tokens/personal data into it or `localStorage`.
- Forms: React Hook Form + Zod (UX only; the API always re-validates with FluentValidation). Map API `errors` to fields with `setError`.
- Integration tests run against real PostgreSQL (Testcontainers), never the EF in-memory provider.

## Business rules (decided)
- One email = one account = one profile.
- An owner may have several clubs; a club has exactly one owner.
- Cancellation window per club, default 24 h; the window in force at cancellation time applies.
- Bookings from today up to 30 days ahead. Slot length per pitch: 30, 60 or 90 min.
- Bookings are never deleted; they change status: Confirmed, CancelledByPlayer, CancelledByClub, NoShow (UI shows pt-PT labels).
- Each booking stores the price at booking time. No payments in the MVP.
- Dates stored in UTC, shown in Lisbon time regardless of the browser time zone.
- Another owner's resources return 404 (don't reveal existence); wrong profile returns 403.

## Conventions
- Code, identifiers, commits, branch names: **English**. User-facing UI text and API error messages: **Portuguese (pt-PT)**.
- Commits: Conventional Commits with the story ID: `feat(US18): ...`, `test(T03): ...`, `fix(US19): ...`.
- Branches: `feat/US18-short-name` (`chore/T01-...` for technical stories). `main` is protected; merge via PR with green CI (squash).
- Each bug gets a reproducing test first, then the fix.

## Commands
See [README.md](README.md) for full setup.
- Everything: `docker compose up --build` (app :3000, API :5080, Swagger :5080/swagger, Mailpit :8025, Postgres :5432)
- Backend build: `dotnet build backend/SportsBooking.slnx` (also regenerates `backend/src/SportsBooking.Api/openapi/sportsbooking-api.json`; commit it)
- Backend tests (Docker must be running, Testcontainers): `dotnet test backend/SportsBooking.slnx`. Uses Microsoft.Testing.Platform (opted in via `backend/global.json`); filter with `dotnet test --project <proj> -- --filter-method "*Name*"`.
- API locally: `dotnet run --project backend/src/SportsBooking.Api` (needs `docker compose up -d postgres`)
- New migration (from `backend/`, local tool): `dotnet ef migrations add <Name> --project src/SportsBooking.Infrastructure --startup-project src/SportsBooking.Api --output-dir Persistence/Migrations`
- Frontend (in `frontend/`): `npm run dev`, `npm run lint`, `npm run typecheck`, `npm test`, `npm run build`
- Regenerate API types after a backend build: `npm run gen:api` in `frontend/` (commit `src/lib/api/schema.d.ts`; CI checks drift)

## Gotchas
- Next.js 16.4 differs from older versions (e.g. `error.tsx` gets `retry`, `cacheComponents` is on, `proxy.ts` replaces `middleware.ts`). Read [frontend/AGENTS.md](frontend/AGENTS.md) and the bundled docs in `frontend/node_modules/next/dist/docs/` before writing frontend code.
- Build-time OpenAPI generation starts the app as `GetDocument.Insider`; `Program.cs` gives it a placeholder connection string and skips migrations. Keep startup code safe for that.
- EF migrations folder is marked `generated_code` (its own `.editorconfig`); test projects allow underscores in test names (CA1707 off).

## Definition of done
A story is done only when every acceptance criterion is covered by tests (unit, integration and/or component) and the CI is green.
