# Stack and Good Practices — Booking App
 
Oct 22, 2026 · @Rodri
 
Next.js (App Router) on the front end, ASP.NET Core on the back end and PostgreSQL, served on the same origin and with the session in httpOnly cookies. The API is the sole owner of the business rules; Next.js only presents.
 
## Architecture
 
The architecture is described, with C4 diagrams and 4+1 views, in [docs/architecture/README.md](architecture/README.md); the inventory of all the elements is [docs/architecture/inventory.md](architecture/inventory.md). Note: Caddy is the production design (T06); today, in development, the browser talks to Next.js, which forwards `/api/*` to the API.
 
The browser only talks to Caddy. Server Components call the API from the inside, passing on the user's cookie, and nothing but the API accesses PostgreSQL.
 
## Stack
 
Each choice has a reason; what has no reason to be here was left out.
 
| Layer | Technology | Why |
| --- | --- | --- |
| Front end | Next.js (App Router), React, TypeScript in strict mode | Server Components read data on the server with the cookie; Client Components handle interaction |
| UI | Tailwind CSS and shadcn/ui | Tailwind shows up in job listings; shadcn/ui gives accessible components that live in your own code |
| Server data on the client | TanStack Query | Cache, refetching and invalidation after booking or cancelling |
| UI state | The page address (`useSearchParams`) | The booking step and the calendar view live in the URL, not in a store (see State) |
| Forms | React Hook Form and Zod | Validation in the browser with per-field messages |
| API types | Generated from OpenAPI (openapi-typescript) | The C# DTOs are the single source of truth; the front end does not rewrite types by hand |
| Charts | Recharts | Admin charts |
| Back end | ASP.NET Core 10 (LTS), Web API with controllers | Controllers are what most .NET job listings use |
| Users | ASP.NET Core Identity (core only, no UI) | Password hashing, lockout after failed attempts and recovery tokens without reinventing them |
| Session | Short-lived JWT and refresh token, both in httpOnly cookies | See Authentication |
| API validation | FluentValidation | Request rules outside the controllers and testable |
| Data access | EF Core with Npgsql | Migrations, LINQ and the equivalent of the Prisma you have already used |
| Database | PostgreSQL | Exclusion constraints to prevent overlapping bookings |
| Background work | BackgroundService and an outbox table | Emails and reminders without blocking requests and without getting lost |
| Email | SMTP: Brevo in production, Mailpit in development | No real email goes out during development |
| Tests | xUnit, Testcontainers, Vitest, React Testing Library, MSW | See Tests |
| Infrastructure | Docker, docker compose, GitHub Actions, Caddy | Everything starts with one command; Caddy handles HTTPS and puts the front end and the API on the same origin |
 
## Back end: architecture and dependency injection
 
Clean Architecture in four projects, with dependencies pointing inward only: Api → Infrastructure → Application → Domain.
 
```text
backend/
  src/
    SportsBooking.Domain/          entities, business rules, no dependencies
    SportsBooking.Application/     use cases (services), DTOs, validators, interfaces
    SportsBooking.Infrastructure/  EF Core, migrations, Identity, JWT, email, outbox
    SportsBooking.Api/             controllers, authentication, ProblemDetails, DI composition
  tests/
    SportsBooking.UnitTests/
    SportsBooking.IntegrationTests/
```
 
**Pragmatism, without too much ceremony**
 
- No generic repository on top of EF Core: the `DbContext` is already the unit of work. Application uses an `IAppDbContext` interface; a repository only appears where the query is complex.
- Simple application services (`BookingService.CreateAsync`) instead of MediatR, and hand-written mapping instead of AutoMapper. Both libraries got a commercial licence in 2025, and they are not needed here.

**Dependency injection**
 
- ASP.NET Core's built-in container and constructor injection only; no asking `IServiceProvider` for services in business code.
- Each layer registers itself: `services.AddApplication()` and `services.AddInfrastructure(config)`; `Program.cs` only composes.
- Lifetimes: `DbContext` and the services that use it are Scoped; stateless helpers are Singleton. Never inject a Scoped into a Singleton: a `BackgroundService` creates its own scope with `IServiceScopeFactory`.
- The current time comes from an injected `TimeProvider`, never from `DateTime.UtcNow`; tests use a fake clock (cancellation window, reminders).
- Configuration with the Options pattern (`IOptions<JwtOptions>`), validated at startup with `ValidateOnStart`.

**Code rules**
 
- Thin controllers: they receive the DTO, call the service and return the response. No EF Core in controllers.
- Entities protect their rules: private setters and intention-revealing methods, such as `booking.CancelByPlayer(window, now)` and `booking.CancelByClub(reason, now)`, which return the result (`CancellationOutcome`, `ClubCancellationOutcome`) and only change the state when the rule allows it.
- Expected failures (slot taken, outside the window) are results returned by the service, not exceptions; exceptions are for the unexpected.
- `async` all the way, with `CancellationToken` down to EF Core.
- Nullable reference types on and warnings treated as errors.
- Public IDs are GUID v7 (`Guid.CreateVersion7()`): they cannot be guessed like a sequential number and they index well.

## Authentication: httpOnly cookies
 
The browser's JavaScript never touches the tokens: they stay in httpOnly cookies, which an XSS attack cannot read. To know who is signed in, the front end calls `GET /api/auth/me`.
 
**The two cookies**
 
| Cookie | Content | Lifetime | Attributes |
| --- | --- | --- | --- |
| `access_token` | JWT with `sub` and `role`, no personal data | 15 minutes | HttpOnly, Secure, SameSite=Lax, Path=/ |
| `refresh_token` | Opaque random value, stored in the DB only as a hash | 7 days | HttpOnly, Secure, SameSite=Strict, Path=/api/auth |
 
**Flow**
 
1. `POST /api/auth/login`: the API validates the credentials with Identity and answers with the two cookies.
2. Every request carries the `access_token` automatically; ASP.NET Core's JwtBearer reads it from the cookie (`OnMessageReceived` event).
3. On a 401, the client calls `POST /api/auth/refresh` once and repeats the request. Only one refresh is in progress at a time: within a tab, requests share the same refresh; between tabs, the Web Locks API (`navigator.locks`) lets one tab at a time renew, and whoever gets the lock first repeats the original request, because another tab may already have renewed (the cookies are shared). Without `navigator.locks`, only the per-tab rule applies.
4. Each refresh rotates the token: the old one becomes invalid and a new one is issued. If an already used token shows up again on refresh, all of that user's tokens are revoked (a theft signal); the same happens when `POST /api/auth/logout` receives an already used token, and when refresh is requested for a deactivated account.
5. `POST /api/auth/logout` revokes the refresh token in the DB and deletes the two cookies.

**Same origin, mandatory**
 
The front end and the API have to be on the same site: Caddy (in production) or the Next.js rewrites (in development) send `/api/*` to .NET. That way the cookies are first-party and there is no CORS. A front end on a `vercel.app` domain and an API on another domain make the cookies third-party, and browsers block them.
 
**In Next.js**
 
- Server Components read the cookie with `cookies()` and pass the `Cookie` header when they call the API.
- Server Components cannot write cookies, and the server never renews the session. Decided (US03): the `refresh_token` cookie has `Path=/api/auth`, so page requests do not carry it and Next.js has no way to refresh. There is no token renewal in `proxy.ts`; refreshes always come from the browser.
- The server guards live in the layouts of the areas (`/manage` for the Owner, `/admin` for the Admin) and, for the player's bookings, in the layout of `/bookings` (`PlayerGuard`): a helper in `lib/server` (`import 'server-only'`) calls `GET /api/auth/me` at the API's internal URL with the user's `Cookie` header and `cache: 'no-store'`. No valid session → `/login?next=<area path>`; session of another profile → that user's area. With `cacheComponents`, the guard reads cookies, so it sits inside a `<Suspense>`. The public pages (`/`, search, club pages) stay open to everyone.
- `proxy.ts` (`src/proxy.ts`) puts the requested path (with the query) in the `x-pathname` request header, always over any value sent by the client, for `/manage`, `/admin` and `/bookings`. The guards read it to build `next=<full path>`, which goes through `safeNext`. It does not renew tokens, does not redirect and does not decide access. It also does a second thing, for the HTTP status: it answers **404** to an id that cannot exist (a malformed GUID in `/manage/clubs/{id}…` and `/admin/owners/{id}`, an impossible slug in `/clubs/{slug}`) and to a public club that the API does not have (`GET /api/clubs/{slug}` with 404). A page that streams (`loading.tsx`, `<Suspense>`) fixes the 200 on the first byte, and a `notFound()` after that shows the page but keeps the 200 (with `noindex`); Next.js documents the check in the Proxy, before the page starts sending. The club is requested from the API once a minute per slug (process memory), only on real page loads (not on prefetches or client navigations) and, if the API fails or takes longer than 3 s, the page goes through and decides for itself (never an error because of the proxy). Known limit: a well-formed GUID that does not exist (another owner's club, for example) is only known with the session, which the proxy does not read; it still answers 200 with the client's "not found" screen (these are pages behind a login). The status is tested in `src/proxy-not-found.test.ts` and in `frontend/scripts/check-not-found.mjs`, which runs in CI against the build with a simulated API.
- An expired access token leads to `/login?next=...`. The login page first tries, in the browser, a silent `POST /api/auth/refresh`: with 204 it goes on to `next` (if it is a relative path in the user's area; otherwise, the profile's home page), otherwise it shows the form.
- The guards are only a convenience (redirects). Whoever decides access is always the API (401/403).

**CSRF and brute force**
 
- Since the browser sends cookies by itself, requests that change data need protection: SameSite=Lax blocks POSTs coming from other sites, and the API rejects POST, PUT, PATCH and DELETE whose `Origin` header is not the site's.
- Identity account lockout after several failed attempts and rate limiting (built-in middleware) per IP on login, the two registrations, refresh and the two password recovery steps (not on logout or on `GET /api/auth/me`) and on the public read endpoints (search, localities, club page and slots).
- Deactivating an owner (US29) or changing the password revokes the refresh tokens; the session ends at most when the access token expires (15 minutes).
- `POST /api/auth/refresh` always checks that the account is active before issuing tokens; a deactivated account gets 401 and does not renew the session.
- **Known trade-off:** the access token is a stateless JWT, validated without going to the database. An already issued token stays valid until it expires (15 minutes at most), even after the account is deactivated. The short lifetime and the check on refresh limit that window. There is no active-account check on every request.

## Front end: server and client
 
Server Components by default; `'use client'` only where there is interaction, and as low as possible in the tree.
 
| Where it runs | What for | Examples | How it gets data |
| --- | --- | --- | --- |
| Server Component | Showing data on the first render | Club page, admin totals | `fetch` to the internal API with the `Cookie` header, no cache (`cache: 'no-store'`) |
| Client Component | Interaction | Forms, booking flow, `As minhas reservas` (My bookings), calendar navigation, charts, filters | TanStack Query over `/api/*` (same origin, the cookies go along by themselves) |
 
**Rules**
 
- Code that reads cookies or uses the API's internal URL imports `server-only`, so it never ends up in the browser bundle.
- Each area has its own `layout.tsx`, which on the server calls `/api/auth/me` and redirects if the profile does not match. It is a convenience: the API still decides access.
- `loading.tsx` and `error.tsx` in each route segment; mutation errors appear as a toast.
- Only `NEXT_PUBLIC_*` variables reach the browser; no secret uses that prefix.

**State: TanStack Query and the page address**

- Data that comes from the API stays in TanStack Query. After booking or cancelling, it invalidates the affected queries (free slots, my bookings); on a 409, it reloads the slots.
- **There is no Zustand.** What the user chooses lives in the page address, read with `useSearchParams` and written with `router.replace`: in booking `?pitch=&date=&slot=&weeks=` (`features/booking/selection.ts`), in the calendar `?date=&view=&pitch=&cancelled=&booking=`, in searches and lists the filters and the page. There are two reasons. Someone without a session who is sent to login (`/login?next=<this address>`) comes back to the same club, pitch, date and slot. And the link is shareable and works with the "Back" button and on reload. An in-memory store lost the choice on every redirect and created a second version of the truth.
- Default values are not written in the address (today, one week, the first page) and a meaningless value counts as the default.
- Never copy API data into other state; never tokens or personal data in `localStorage`.
- If one day shared UI state is needed that makes no sense in the address, a small store per feature is used, only in Client Components and created per request if it is needed in SSR; today no feature asks for it.

**Forms with Zod**
 
- React Hook Form with `zodResolver`; one schema per form, in the feature's folder.
- Zod serves the user experience, not security: the API always validates again.
- The API's validation errors (`errors` in the ProblemDetails) are put on the right field with `setError`.

**Structure**
 
```text
frontend/src/
  app/
    (player)/      home and search, clubs/[slug], bookings (my bookings)
    manage/        owner area (clubs, calendar, statistics)
    admin/         admin area (dashboard, owners/[id])
    login/  register/ (and register/owner)  forgot-password/  reset-password/
  proxy.ts         writes the requested path to the x-pathname header (return after login) and answers 404 to ids that do not exist
  features/
    auth/  catalog/  booking/  my-bookings/
    clubs/  pitches/  calendar/  statistics/  admin/
                   each with components/, schemas/ when there are forms, api.ts and tests next to them
  lib/
    api/           types generated from OpenAPI, browser client, messages by code
    server/        API calls from the server (server-only)
    auth/          redirect decisions (pure functions)
    datetime.ts    dates always in Lisbon time
  components/      area headers and guards, form fields, period filter
  components/ui/   shadcn/ui components
```
 
## Data: EF Core, PostgreSQL and concurrency
 
The app's most important rule, never two bookings on the same pitch and time, lives in the database and not only in the code.
 
**Overlapping bookings (T03)**
 
An exclusion constraint, created in a migration with `migrationBuilder.Sql`:
 
```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;
 
ALTER TABLE bookings ADD CONSTRAINT bookings_no_overlap
  EXCLUDE USING gist (
    pitch_id WITH =,
    tstzrange(starts_at, ends_at) WITH &&
  ) WHERE (status = 'Confirmed');
```
 
- The status is stored as text (`HasConversion<string>()`) so the condition works.
- A violation gives PostgreSQL error `23P01` (exclusion\_violation), which the API translates into 409.
- Blocks and bookings are in different tables, and the constraint does not cover both. Creating a booking and creating a block both start by locking the pitch's row (`SELECT … FOR UPDATE`) inside the transaction, and only then check the overlap.

**Dates (T04)**
 
- `timestamptz` columns and `DateTime` always in UTC in C# (Npgsql refuses a `DateTime` without UTC `Kind` in those columns).
- Opening hours as weekday and `TimeOnly`, plus the club's time zone (`Europe/Lisbon`).
- Slot generation converts local time to UTC with `TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon")`.
- Tests for the 2026 clock changes: 29 March (the hour between 1:00 and 2:00 does not exist) and 25 October (the hour between 1:00 and 2:00 repeats).
- Decided rules: a local time that does not exist moves forward past the gap (01:30 becomes 02:30); a time that repeats uses the first occurrence (summer time). Slots are blocks of real time from opening, so on those days there is one slot fewer or one more. In the repeated hour, the UI shows `(verão)` ("(summer)") or `(inverno)` ("(winter)").
- Known MVP limitation: the latest closing time is 00:00; schedules that go past midnight are not supported.

**Queries**
 
- Reads with `AsNoTracking()` and direct projection to DTOs with `Select`; never return entities in the API.
- Lists always paginated and with a defined order.
- Indexes on the filter columns: bookings by pitch and start, bookings by player and start, clubs by locality.
- Admin statistics with `GROUP BY`, `COUNT(DISTINCT …)` and `date_trunc`; periods without bookings are filled with zero (in the service, or with `generate_series` in SQL).
- Money in `numeric(10,2)` and `decimal` in C#, never `float`.

**Migrations**
 
- Versioned in the repository; an already applied migration is never edited, it is fixed with another one.
- In production they are applied on deploy with a migration bundle (`dotnet ef migrations bundle`), not at API startup.
- Demo data (T05) stays outside the migrations.

## Validation and errors
 
Three layers of validation, each with its own role, and all API errors in the same format: ProblemDetails (RFC 9457).
 
| Layer | Tool | Validates | Example |
| --- | --- | --- | --- |
| Browser | Zod | Shape of the data, for immediate feedback | Valid email, password with 8 characters |
| API | FluentValidation | Shape of the requests, always, wherever they come from | End date after the start date |
| Domain | Entity and service methods | Business rules | Cancellation within the window, free slot |
 
**HTTP codes**
 
| Code | When |
| --- | --- |
| 400 | Invalid request; the ProblemDetails carries `errors` per field |
| 401 | No session or expired token |
| 403 | Valid session, wrong profile |
| 404 | Does not exist, or belongs to another owner |
| 409 | Conflict: slot taken, overlap with a block |
| 422 | Business rule violated: cancellation outside the window, inactive pitch |
| 429 | Too many requests (rate limiting) |
| 500 | Unexpected error: logged, with no internal details in the response |
 
**Rules**
 
- `AddProblemDetails()` and a global `IExceptionHandler`; no controller has a `try/catch` for generic errors.
- Each error carries a stable `code` (for example, `slot_taken`, `outside_cancellation_window`) and the `traceId`. The front end picks the message by `code`, never by the text.
- No stack traces or exception messages in responses in production.

## Tests
 
Integration tests run against a real PostgreSQL, because the most important rules (overlap, time zones) only exist there.
 
| Type | Tools | What it covers | Examples |
| --- | --- | --- | --- |
| Unit (back end) | xUnit, Shouldly, `FakeTimeProvider` | Domain and service rules | Cancellation window, slot generation on clock changes |
| Integration (back end) | xUnit, `WebApplicationFactory`, Testcontainers (PostgreSQL) | Complete endpoints, from authentication to the database | Owner A does not see owner B's data (T02); simultaneous requests for the same slot (T03); error codes and `code`s |
| Components (front end) | Vitest, React Testing Library, MSW | UI behaviour with the API simulated | Per-field errors on registration; message and updated list after a 409 |
| End-to-end | Playwright (Chromium), docker compose with the demo data | Four flows: the player books and cancels; two players want the same time; the owner sees the booking and a non-existent club gives 404 (T08, `e2e/`); a new account exports its data, deletes itself and can no longer sign in (US30) | The player cancels; the second player gets the 409 `slot_taken` |
 
**End-to-end.** There are four Playwright tests in `e2e/` (T08, [ADR 0010](architecture/adr/0010-playwright-end-to-end-tests.md)): Chromium only, against the repository's `docker compose` with the demo data, in a separate workflow (`e2e.yml`) so it does not slow down the deploy. They run locally with `npm run e2e` in `e2e/` (README, "Running the E2E tests"). The remaining flows and screens are still checked by hand in the browser before each merge of a story with an interface (with the stack and Mailpit, or with temporary scripts that do not stay in the repository), and the emails and logs are checked; end-to-end flows are not added without need, because each one costs CI time and a risk of instability.
 
**Rules**
 
- EF Core's in-memory provider is no good for integration: it does not apply constraints or transactions like PostgreSQL.
- Each bug found first gets a test that reproduces it, and only then the fix.
- No percentage coverage target: what has to be covered are the business rules and the error cases.
- Avoid FluentAssertions 8, which moved to a commercial licence; Shouldly (or xUnit's `Assert`) is enough.

## Security: checklist
 
To tick off as it gets done.
 
- [x] Fallback policy that requires authentication on every endpoint; the public ones (search, club page, login, registration) carry `[AllowAnonymous]` explicitly. (US01)
- [x] Authorization by profile and by owner on every club endpoint, with tests (T02, `OwnedClubs`; the access test discovers new endpoints by itself). (US06)
- [x] httpOnly, Secure and SameSite cookies as in the authentication section; `Origin` check on POST, PUT, PATCH and DELETE. (US01)
- [x] Admin account only by seed; no path in the API gives the Admin profile (US04, with a guard test).
- [x] Identity account lockout and rate limiting on the authentication routes (login, registration, refresh and the two password recovery steps). (US03, US05)
- [x] Generic messages on login and password recovery (they do not reveal whether the email exists). (US03, US05: the request always answers the same 202 and the confirmation always gives the same 400 for a bad link.)
- [x] HTTPS everywhere and HSTS in production. Verified on 2026-11-10: the published site sends `Strict-Transport-Security: max-age=63072000; includeSubDomains; preload`, a value that comes from the platform (Vercel), not from the code (which writes `max-age=31536000`). `ENABLE_HSTS=true` at build time is the application's own option (off by default); it was not verified whether it is on in the production build. The API does not send HSTS (see the backlog, T06).
- [x] Security headers: Content-Security-Policy, X-Content-Type-Options, Referrer-Policy, Permissions-Policy and `frame-ancestors` on the site; `X-Content-Type-Options` and no `Server` on the API (T06, `frontend/src/lib/security-headers.ts`, tested). `/reset-password` keeps `Referrer-Policy: no-referrer` and `Cache-Control: no-store`. **Known limit:** the policy allows `'unsafe-inline'` in scripts and styles (see the backlog, T06).
- [x] Secrets in environment variables (user-secrets in development); `.env` in `.gitignore`. The code reads the JWT key, the admin account, SMTP and the database connection from environment variables, and the API does not start with required settings missing (tested with the options validators); the only key in the repository is the development one, which is only good in development. Configuring the secrets in production is T06.
- [x] Hand-written SQL only with parameters (`FromSql`, `SqlQuery` and `ExecuteSql` with interpolation, which parameterizes); never concatenate strings. All the hand-written SQL in Infrastructure is like this.
- [x] No passwords, tokens or cookies in the logs. Tested where the risk exists: the admin command, bookings and emails (no address, body or secret reaches the logs) and password recovery.
- [x] The admin sees only counts, never players' names or emails. Every endpoint of the area has a test that looks for a player's name, email and id in the response.
- [x] `dotnet list package --vulnerable` and `npm audit` in CI.
- [x] Dependabot on (`.github/dependabot.yml`: NuGet, npm, GitHub Actions and the back end and front end `Dockerfile`s, every week, one PR grouped per ecosystem for minor and patch updates, with a 14-day wait for new versions; major updates come one by one and are reviewed by hand). (T06)

## Repository, Git, CI and deploy
 
A single repository, `main` changed only by pull requests (no branch protection on GitHub, see below) and automatic deploy of everything that passes CI.
 
```text
/
  backend/
  frontend/
  docs/                backlog, C4 diagrams, decisions (ADRs)
  docker-compose.yml
  Caddyfile
  .github/workflows/   ci.yml, deploy.yml
```
 
**Git**
 
- The rule is that `main` only receives pull requests with green CI, even when working alone. **Today there is no branch protection on `main`** (no rule in GitHub's settings): green CI is checked by hand before each merge.
- Short branches with the story ID (`feat/US18-book-slot`) and squash merge.
- Commits in Conventional Commits format (`feat:`, `fix:`, `test:`), with the story ID.
- Important decisions in short ADRs in `docs/architecture/adr/` (one app instead of two, cookies instead of localStorage, the exclusion constraint).

**CI (GitHub Actions)**
 
- Back end: restore, build and tests, including the integration ones with Testcontainers (GitHub's Ubuntu runners have Docker).
- Front end: lint, type check, tests and build.
- End-to-end tests (Playwright) in a separate workflow, `e2e.yml`, which the deploy does not wait for (see Tests).
- Check for vulnerable packages in both.

**Deploy**
 
- A virtual machine with docker compose: Caddy, Next.js (image with `output: 'standalone'`), API and PostgreSQL.
- CI builds the images and publishes them to the GitHub Container Registry; the deploy comes in over SSH, applies the migration bundle and runs `docker compose up -d`.
- Caddy handles the HTTPS certificates and forwards `/api/*` to the API and the rest to Next.js, on the same origin.
- Daily database backup with `pg_dump`.
- Alternative, if you want Azure on your CV: Azure Container Apps with Azure Database for PostgreSQL. Check the costs and the free tiers before choosing.

## Sources
 
- [MediatR and MassTransit Going Commercial: What This Means For You](https://www.milanjovanovic.tech/blog/mediatr-and-masstransit-going-commerical-what-this-means-for-you) — MediatR and AutoMapper move to a commercial licence (2025).
- [Fluent Assertions shifts to restrictive license](https://www.devclass.com/development/2025/01/16/another-open-source-project-shifts-to-restrictive-license-fluent-assertions-following-xceed-partnership/1621343) — version 8 requires a paid licence for commercial use; version 7 stays free.
- [Next.js Proxy Replaces Middleware](https://johnkavanagh.co.uk/articles/next-js-proxy-replaces-middleware/) — in Next.js 16, `middleware.ts` becomes `proxy.ts`.

The rest of the document comes from general knowledge of the tools, with no specific source; check the versions in the official documentation when you create the project.
