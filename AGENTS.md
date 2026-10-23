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
- Time (T04): convert local↔UTC only via `SportsBooking.Domain.Scheduling.ClubTime`. `Today()` gives the Lisbon date. Decided DST rules:
  - A local time that does not exist (clocks go forward, last Sunday of March) moves forward past the gap: 01:30 → 02:30 WEST.
  - A local time that happens twice (clocks go back, last Sunday of October) uses its first occurrence (WEST).
  - Slots come only from `SlotGenerator`: blocks of real elapsed time from opening, so DST days have one slot fewer/more and every slot lasts exactly its duration.
  - In the UI, times inside the repeated October hour are labelled "(verão)" / "(inverno)" via `formatLisbonSlotTime`; slots are never hidden.
- Opening hours: closing `00:00` = midnight. Hours past midnight (e.g. 22:00–02:00) are rejected; known MVP limitation.
- Frontend shows dates only through `frontend/src/lib/datetime.ts` (Lisbon, pt-PT); never `toLocaleString()` without `timeZone`.
- Overlapping bookings are prevented by a PostgreSQL exclusion constraint (`23P01` → 409), not only app code.
- Errors: ProblemDetails (RFC 9457) via `AddProblemDetails()` + global `IExceptionHandler`. Every error has a stable `code` (e.g. `slot_taken`) and `traceId`; the front end picks the message by `code`. Status codes: see table in docs/stack.md.
- Auth: ASP.NET Core Identity (core only). JWT access token (15 min) + rotating refresh token (7 days), both **httpOnly cookies**; front end learns the user via `GET /api/auth/me`. On a 401 the client calls `POST /api/auth/refresh` once and retries; login only if refresh fails. Refresh **must** reject deactivated accounts. Accepted trade-off: an already-issued access token stays valid up to 15 min after deactivation (no per-request active check). Front end and API are **same origin** (Next.js rewrites `/api/*` in dev, Caddy in prod); no CORS. API checks `Origin` on mutating requests.
- Accounts (US04): Identity core with `AppUser` (Infrastructure/Identity), snake_case tables (`users`, `roles`, `user_roles`, …) via `UseSnakeCaseNamingConvention()`, unique index on the normalized email. Profiles are the roles `Player`/`Owner`/`Admin` (`Domain.Accounts.Profiles`), seeded by migration. Password rule: `PasswordPolicy` (≥ 8 characters, no composition rules). Hashes: Identity v3 PBKDF2-HMAC-SHA512, 210,000 iterations (`PasswordHasherOptions`); older hashes are upgraded on the next successful check.
- Admin seed (US04): `AdminAccountSeeder` runs at startup in **every** environment, after migrations (production applies them before the API starts), from `AdminSeed:Email`/`AdminSeed:Password` (env `AdminSeed__*`, compose `ADMIN_SEED_*`, user-secrets locally). It is the **only** code allowed to assign the Admin profile (guarded by `AdminProfileGuardTests`). Decided rules:
  - Both settings absent → API starts, no admin, warning logged.
  - Only one set, malformed email or password < 8 characters → `AdminSeedOptionsValidator` fails `ValidateOnStart` and the API refuses to start, before touching the database. The error names the settings, never their values.
  - Surrounding whitespace on the email is trimmed before validating; everything else is strict (the password is used as is).
  - An existing admin is never modified (changing the password setting later has no effect); never a second admin, even with another email; an existing player/owner email is never promoted (warning logged, without the email). Never log the email or password.
  - Changing the admin password is manual (README, "Changing the admin password"): delete the admin row and restart with new settings.
- Sessions (US01): `ISessionService` (Application/Auth) implemented by `Infrastructure/Auth/SessionService`. Access token = HS256 JWT with only `sub` + `role` (`Jwt:Issuer`/`Audience` in appsettings, `Jwt:SigningKey` ≥ 32 bytes from env `Jwt__SigningKey`; Development has a local-only key). Refresh token (cookie `Path=/api/auth`, so refresh and logout both receive it) = 32 random bytes, stored only as SHA-256 in `refresh_tokens`; rotation is an atomic conditional update; a used token presented again revokes all sessions of the account; lifetime is absolute (rotation keeps the expiry). Deactivated = `users.deactivated_at` set (`AppUser.Deactivate(now)`). Cookies are written only by `Api/Auth/AuthCookies` (attributes from docs/stack.md); JwtBearer reads the `access_token` cookie.
- Authorization: fallback policy requires a session on every endpoint; public ones need `[AllowAnonymous]` (health, OpenAPI, register, refresh). Side effect: anonymous requests to unknown routes get 401, not 404.
- Origin check (`Api/Security`): POST/PUT/PATCH/DELETE need an `Origin` equal to one of `Security:AllowedOrigins` (Development: :3000 and :5080; production via `Security__AllowedOrigins__0`), else 403 `origin_not_allowed`. A missing `Origin` is rejected too, so server-side callers (e.g. Next.js server code) must send it.
- Expected failures: services return `Result<T>` / `ServiceError` (`Application/Common/Result.cs`); controllers turn them into ProblemDetails with `this.Problem(error)` (`Api/ErrorHandling/ServiceErrorResults`). Validation = FluentValidation in the service, field keys camelCase (`ToFieldErrors()`), 400 `validation_failed`. Request DTO properties stay non-nullable; MVC's implicit `[Required]` is off so missing fields get the pt-PT FluentValidation messages. `ProblemDetailsSchemaTransformer` documents `code`/`traceId` in OpenAPI.
- Registration (US01): `POST /api/auth/register` → `PlayerRegistrationService` → `IAccountStore.CreatePlayerAsync` (Identity; account + Player profile in one transaction; the unique email index settles races). Taken email = 409 `email_taken` with one fixed message for any profile (US01 requires saying it; see the trade-off in docs/backlog.md). Shared rules in `Domain/Accounts` (`EmailAddressPolicy`, `AccountNamePolicy`, `PhoneNumberPolicy`, `PasswordPolicy`), also used by the admin seed.
- Frontend: Next.js App Router, TypeScript strict, Tailwind + shadcn/ui. Server Components by default; `'use client'` as low as possible. Server code that reads cookies or the internal API URL imports `server-only`. API types generated from OpenAPI (openapi-typescript), never hand-written.
- State: **TanStack Query** for anything from the API (invalidate after mutations, refetch slots on 409). **Zustand** only for shared UI state (booking step, calendar view); never copy API data or tokens/personal data into it or `localStorage`.
- Forms: React Hook Form + Zod (UX only; the API always re-validates with FluentValidation). Map API `errors` to fields with `setError`. Zod schemas mirror the API's limits and pt-PT messages (e.g. `features/auth/schemas/register.ts`). Use `noValidate` so browser validation does not replace ours. Pages stay mounted across navigations (`cacheComponents`), so reset forms with secrets after submit.
- Frontend API access: only via `src/lib/api/client.ts` (`apiRequest`/`apiJson`): same-origin `/api/*`, ProblemDetails → `ApiError` (`status`, `code`, `fieldErrors`, `traceId`), one shared refresh on 401 then one retry. Messages by code in `src/lib/api/error-messages.ts`; unexpected mutation errors go to a toast (sonner). Current user: `useCurrentUser()` (`features/auth`), query key `["auth", "me"]`.
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
- Backend tests (Docker must be running, Testcontainers): `dotnet test backend/SportsBooking.slnx`. Uses Microsoft.Testing.Platform (opted in via the repo-root `global.json`); filter with `dotnet test --project <proj> -- --filter-method "*Name*"`.
- API locally: `dotnet run --project backend/src/SportsBooking.Api` (needs `docker compose up -d postgres`)
- New migration (from `backend/`, local tool): `dotnet ef migrations add <Name> --project src/SportsBooking.Infrastructure --startup-project src/SportsBooking.Api --output-dir Persistence/Migrations`
- Frontend (in `frontend/`): `npm run dev`, `npm run lint`, `npm run typecheck`, `npm test`, `npm run build`
- Regenerate API types after a backend build: `npm run gen:api` in `frontend/` (commit `src/lib/api/schema.d.ts`; CI checks drift)

## Gotchas
- Next.js 16.4 differs from older versions (e.g. `error.tsx` gets `retry`, `cacheComponents` is on, `proxy.ts` replaces `middleware.ts`). Read [frontend/AGENTS.md](frontend/AGENTS.md) and the bundled docs in `frontend/node_modules/next/dist/docs/` before writing frontend code.
- Build-time OpenAPI generation starts the app as `GetDocument.Insider`; `Program.cs` gives it placeholder settings and skips migrations. Keep startup code safe for that.
- Integration tests: the shared `ApiFactory` host applies migrations on startup (`Database:ApplyMigrationsOnStartup=true`, as Development does). For startup behaviour (seeding), use `factory.CreateIsolatedApp(databaseName, settings)`: own database, captured logs via `GetFakeLogCollector()`.
- Integration test clients: the site origin is `https://localhost` (`ApiFactory.SiteOrigin`; HTTPS so Secure cookies work). `factory.CreateBrowserClient()` = cookie jar + `Origin`; `factory.CreateSiteClient()` = `Origin`, no jar (send cookies with `request.WithCookies(...)`, read them with `response.SetCookies()`); `factory.CreateSignedInPlayerAsync()` gives a player and session tokens without slow password hashing.
- Build-time OpenAPI generation also runs `ValidateOnStart`: every new required setting needs a placeholder in the `isOpenApiGeneration` block of `Program.cs`.
- Frontend component tests: MSW (`src/test/msw.ts`) is global and fails on unhandled requests; add handlers with `server.use(...)`. Render with `renderWithProviders` (`src/test/render.tsx`) and mock `next/navigation` where a component uses the router.
- `dotnet ef migrations remove` needs a running database; to drop an unapplied migration, delete its two files and restore `AppDbContextModelSnapshot.cs` from git.
- EF migrations folder is marked `generated_code` (its own `.editorconfig`); test projects allow underscores in test names (CA1707 off).

## Definition of done
A story is done only when every acceptance criterion is covered by tests (unit, integration and/or component) and the CI is green.
