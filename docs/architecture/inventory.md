# Element inventory

Catalogue of everything the diagrams can use. Each element has a **stable id**: the texts and the ADRs refer to it by that id, and in the diagrams the id is the PlantUML alias, never drawn text (names and aliases in [section 12](#12-names-and-aliases-in-the-diagrams)). Only what exists in the code on 2026-10-29 goes in (after merge `f4057ca` into `main`); production (T06, ids `PL-`) is in a separate section, at the end.

Paths are relative to the repository root. To avoid repeating long prefixes:

- `Api/` = `backend/src/SportsBooking.Api/`
- `App/` = `backend/src/SportsBooking.Application/`
- `Dom/` = `backend/src/SportsBooking.Domain/`
- `Inf/` = `backend/src/SportsBooking.Infrastructure/`
- `Web/` = `frontend/src/`

**Id convention:** `P-` people, `X-` external systems, `C-` containers, `L-` back end layers (projects), `API-` API components by feature, `CC-` cross-cutting API pieces, `WEB-` front end components, `D-` domain entities and values (and entities persisted in other layers), `DB-` database objects, `DEV-` development view elements (folders, test projects, CI jobs, generated files), `DEP-` deployment nodes and infrastructure that exist today, `SC-` usage scenarios, `PL-` production elements (T06). An id never changes or is reused; an element that ceases to exist is marked as removed.

## 1. People and actors

| Id | Name | Type | Responsibility | Where it appears in the code |
| --- | --- | --- | --- | --- |
| P-VIS | Visitor | Person (no session) | Searches clubs, sees a club's page and the free times; to book, is sent to login. | Public routes `Web/app/(player)/`, `Api/Controllers/ClubsController.cs`, `LocalitiesController.cs` (`[AllowAnonymous]`) |
| P-JOG | Player | Person (`Player` profile) | Registers, books slots (single or weekly), sees and cancels their bookings, receives emails. | `Dom/Accounts/Profiles.cs`; `Api/Controllers/BookingsController.cs`, `MeBookingsController.cs`; `Web/app/(player)/bookings/` |
| P-DONO | Owner | Person (`Owner` profile) | Manages their clubs, pitches, schedules, blocks, price bands, calendar, bookings for customers, no-shows and statistics. | `Api/Controllers/Manage*Controller.cs`; `Web/app/manage/` |
| P-ADMIN | Admin | Person (`Admin` profile) | Platform owner: sees counts, charts and the owners list, deactivates and reactivates owners. Created only by the seed. | `Api/Controllers/AdminController.cs`; `Inf/Identity/AdminAccountSeeder.cs`; `Web/app/admin/` |
| P-CLI | Customer without an account | Person (does not use the system) | A person who phones or turns up at the club; the owner books on their behalf (name and mobile number). Receives no email. | `Dom/Bookings/Booking.cs` (`ForCustomer`); `App/Bookings/ClubBookings.cs` (`CreateAsync`) |
| P-OPS | Operator | Person (whoever runs the application) | Starts the stack, runs the command-line commands (`seed-demo`, `set-admin-password`) and sets the environment variables. | `docker-compose.yml`, `README.md`, `Api/Program.cs` |

## 2. External systems

| Id | Name | Type | Responsibility | Where it appears in the code |
| --- | --- | --- | --- | --- |
| X-SMTP | SMTP server | External system | Receives the application's emails over SMTP (any server; in production it will be Brevo, see PL-BREVO). | `Inf/Notifications/SmtpEmailSender.cs` (MailKit), `EmailOptions.cs` (`Email:*`) |
| X-MAILPIT | Mailpit | External system (development only) | Fake SMTP server with a web interface: catches all emails in development, none goes out. | `docker-compose.yml` (service `mailpit`, `axllent/mailpit:v1.31.6`, ports 1025 and 8025) |
| X-GHA | GitHub Actions | External system | CI (`ci.yml`): build, tests (with Testcontainers), check of the OpenAPI and the generated types, pending migrations, vulnerable packages, lint, types and front end build. It does not deploy; the deploy is `deploy.yml` (PL-DEPLOY). The end-to-end tests are `e2e.yml` (PL-E2E), separately. | `.github/workflows/ci.yml` |
| X-GH | GitHub | External system | Repository and pull requests (squash merge). There is no branch protection on `main`: green CI is checked by hand before each merge. | `README.md`, `AGENTS.md` (conventions) |

## 3. Containers

| Id | Name | Type | Responsibility | Where it appears in the code |
| --- | --- | --- | --- | --- |
| C-WEB | Next.js application | Container (Node.js, Next.js 16 App Router, `standalone` image, port 3000) | Serves the three areas (`/`, `/manage`, `/admin`) and the account pages. Server Components read the internal API; Client Components talk to `/api/*` on the same origin. Forwards `/api/*` to the API (WEB-REWRITE): in development it acts as the reverse proxy. | `frontend/` (`Dockerfile`, `next.config.ts`, `src/`) |
| C-API | ASP.NET Core API | Container (.NET 10, controllers, port 8080; published on 5080 in compose) | Sole owner of the business rules and of database access; cookie authentication, authorization by profile and by owner, ProblemDetails, rate limiting. | `backend/Dockerfile`, `Api/Program.cs` |
| C-WORKER | Email worker (outbox) | Logical container (`BackgroundService` inside the API process) | Reads the outbox, composes and sends the emails over SMTP with retries; runs in each API instance. | `Inf/Notifications/OutboxDispatcher.cs`, `OutboxProcessor.cs` (registered with `AddHostedService`) |
| C-CLI | API command-line commands | Logical container (the same executable as the API, without starting the server) | `seed-demo` (demo data) and `set-admin-password` (changes the admin's password); they run after the migrations and the admin seed and exit. | `Api/Program.cs`; `Inf/Demo/DemoDataCommand.cs`; `Inf/Identity/AdminPasswordCommand.cs` |
| C-DB | PostgreSQL database | Container (PostgreSQL 18.6) | Stores all the data; enforces the rules that cannot fail (no overlapping bookings, append-only owner history) and stores the Data Protection keys. | `docker-compose.yml` (service `postgres`); `Inf/Persistence/` |
| C-BROWSER | User's browser | Runtime environment (outside our deploy) | Runs the Client Components and keeps the httpOnly session cookies; talks only to C-WEB (same origin). | `Web/` (components with `"use client"`), `Web/lib/api/client.ts` |

**Reverse proxy, as it is today:** there is no Caddy in the repository. In development and in compose, the browser talks to C-WEB on port 3000 and Next.js forwards `/api/*` to `http://api:8080` (WEB-REWRITE). The API is also exposed directly on port 5080 (Swagger at `/swagger`), and `http://localhost:5080` is one of the origins accepted in Development. Caddy (PL-CADDY) was not adopted.

## 4. API components by feature

### Layers (back end projects)

| Id | Name | Type | Responsibility | Where it appears in the code |
| --- | --- | --- | --- | --- |
| L-API | Api layer | .NET project | Thin controllers, cookies and authentication, ProblemDetails, HTTP security; `Program.cs` composes. References only L-INF. | `backend/src/SportsBooking.Api/` |
| L-APP | Application layer | .NET project | Services per use case, validation, `Result`, `IAppDbContext` and the interfaces that Infrastructure implements. References only L-DOM. | `backend/src/SportsBooking.Application/` |
| L-INF | Infrastructure layer | .NET project | EF Core and PostgreSQL, Identity, sessions, SMTP, the outbox worker, commands. References L-APP. | `backend/src/SportsBooking.Infrastructure/` |
| L-DOM | Domain layer | .NET project | Entities, values and rules, with no dependencies. | `backend/src/SportsBooking.Domain/` |

### Components

Each component brings together the controller (Api), the services (Application) and what Infrastructure gives it.

| Id | Name | Type | Responsibility | Where it appears in the code |
| --- | --- | --- | --- | --- |
| API-AUTH | Accounts and sessions | Component | Player and owner registration, login and logout, `GET /api/auth/me`, refresh with rotation, attempt lockout, password recovery, exporting the data and deleting the account (US30). | `Api/Controllers/AuthController.cs`, `MeAccountController.cs`; `App/Accounts/`, `App/Auth/`; `Inf/Identity/` (`AccountStore`, `AppUser`, `LoginLockout`, `FallbackPasswordHash`), `Inf/Auth/` (`SessionService`, `RefreshToken`, `PasswordReset`) |
| API-ADMINSEED | Admin seed | Component (startup) | Creates the admin account from `AdminSeed:*` at startup, in every environment; it is the only code that gives the Admin profile. | `Inf/Identity/AdminAccountSeeder.cs`, `AdminSeedOptions.cs`, `AdminSeedOptionsValidator.cs` |
| API-CLUBS | Owner's clubs | Component | Create, list, view and edit the owner's clubs (slug, cancellation window, new-booking notice). | `Api/Controllers/ManageClubsController.cs`; `App/Clubs/` (`ClubService`, `IClubStore`); `Inf/Clubs/` |
| API-CATALOG | Public catalogue | Component | Club search, localities, the club's public page and a pitch's free slots; read-only, with no personal data. | `Api/Controllers/ClubsController.cs`, `LocalitiesController.cs`; `App/Catalog/` (`ClubSearchService`, `PublicClubService`, `VisibleClubs`) |
| API-PITCHES | Pitches | Component | Create, edit, deactivate and reactivate pitches (name unique per club, sport, base price). | `Api/Controllers/ManagePitchesController.cs`; `App/Pitches/PitchService.cs`, `OwnedPitches.cs`, `IPitchStore.cs`; `Inf/Pitches/PitchStore.cs`, `PitchConfiguration.cs` |
| API-SCHED | Schedules and preview | Component | Slot duration and weekly schedule of a pitch; preview of a day's slots for the owner. | `Api/Controllers/ManagePitchScheduleController.cs`; `App/Pitches/PitchScheduleService.cs`, `PitchScheduleRequest.cs` |
| API-BLOCKS | Blocks | Component | Block, list and remove periods of a pitch, refusing overlaps with blocks and bookings. | `Api/Controllers/ManageBlocksController.cs`; `App/Pitches/BlockService.cs`, `BlockRequest.cs`; `Inf/Pitches/BlockConfiguration.cs` |
| API-PRICE | Price bands | Component | Price bands per pitch (days, times, price), with no overlaps, at most 20. | `Api/Controllers/ManagePriceBandsController.cs`; `App/Pitches/PriceBandService.cs`, `PriceBandRequest.cs`; `Inf/Pitches/PitchPriceBandConfiguration.cs` |
| API-BOOK | Player bookings | Component | Book a slot, book a weekly series (preview and creation), my bookings and cancelling. | `Api/Controllers/BookingsController.cs`, `MeBookingsController.cs`; `App/Bookings/BookingService.cs`, `RecurringBookings.cs`, `MyBookings.cs`, `BookingCancellationService.cs`, `BookingSlotCheck.cs`, `IBookingStore.cs`; `Inf/Bookings/BookingStore.cs` |
| API-CLUBBOOK | Owner's calendar and bookings | Component | Daily and weekly calendar, details, cancelling with a reason, booking for a customer, marking and undoing no-shows. | `Api/Controllers/ManageCalendarController.cs`, `ManageBookingsController.cs`; `App/Bookings/ClubCalendar.cs`, `ClubBookings.cs` |
| API-NOTIF | Notifications (outbox) | Component | Puts the emails in the outbox in the same transaction as the change; composes the emails (pt-PT) when they are sent. | `App/Notifications/` (`BookingEmailOutbox`, `BookingEmails`, `BookingNotifications`, `AccountEmails`, `OutboxMessage`); `Inf/Notifications/` (`OutboxProcessor`, `SmtpEmailSender`, `EmailOptions`) |
| API-STATS | Owner's statistics | Component | Occupation, hours, revenue and counts per pitch and per club over a period. | `Api/Controllers/ManageStatisticsController.cs`; `App/Statistics/` (`OccupationService`, `ClubStatisticsService`); `Inf/Statistics/OccupationStore.cs` (`OccupationStore`; the same file holds `RegistrationStatistics`, for the admin's registrations, see API-ADMIN) |
| API-ADMIN | Administration | Component | Totals, bookings and registrations over time, owners list, top 10 by occupation, deactivating and reactivating owners with history. | `Api/Controllers/AdminController.cs`; `App/Admin/`; `Inf/Admin/` (`BookingStatistics`, `OwnerStatusStore`, `OwnerStatusChange`); `Inf/Statistics/OccupationStore.cs` (`RegistrationStatistics`, US28, and `OccupationStore` for the top 10, US27) |
| API-DEMO | Demo data | Component (command) | Creates deterministic owners, players, clubs, pitches, blocks and bookings in an empty database; refuses in production. | `Inf/Demo/DemoDataCommand.cs`, `DemoDataSeeder.cs`, `DemoCatalog.cs` |
| API-ADMINPW | Change the admin's password | Component (command) | `set-admin-password`: reads `ADMIN_NEW_PASSWORD`, changes the hash and revokes the admin's sessions. | `Inf/Identity/AdminPasswordCommand.cs` |
| API-PERSIST | Persistence | Component | `IAppDbContext` (Application's view of the database), `AppDbContext`, migrations, logging of failed commands. | `App/Common/IAppDbContext.cs`; `Inf/Persistence/` (`AppDbContext`, `DatabaseOptions`, `FailedCommandLogger`, `Migrations/`); `Inf/DependencyInjection.cs` |

### Parts of the busiest components

Used in the detail diagrams of the [logical view](4plus1/logical.md). Each part belongs to the component whose id is the prefix of its own.

| Id | Name | Type | Responsibility | Where it appears in the code |
| --- | --- | --- | --- | --- |
| API-BOOK-SINGLE | Single booking | Part of API-BOOK | Booking of a slot by a player. | `App/Bookings/BookingService.cs` |
| API-BOOK-SERIES | Weekly series | Part of API-BOOK | Preview and creation of a series, one date per transaction. | `App/Bookings/RecurringBookings.cs` (`RecurringBookingService`, `RecurringOccurrences`) |
| API-BOOK-MINE | My bookings and cancelling | Part of API-BOOK | List and detail of the player's bookings; cancelling by the player. | `App/Bookings/MyBookings.cs`, `BookingCancellationService.cs` |
| API-BOOK-STORE | Booking store | Part of API-BOOK (also used by API-CLUBBOOK) | Inserts and saves bookings; 23P01 on `bookings_no_overlap` → `false`. | `App/Bookings/IBookingStore.cs`; `Inf/Bookings/BookingStore.cs` |
| API-AUTH-REG | Registration | Part of API-AUTH | Player and owner registration, with session start and with acceptance of the Terms (T07); without it, 400 `terms_not_accepted`. | `App/Accounts/AccountRegistrationService.cs` |
| API-AUTH-LOGIN | Login | Part of API-AUTH | Login with a single response for any failure. | `App/Auth/LoginService.cs` |
| API-AUTH-RESET | Password recovery | Part of API-AUTH | Request and confirmation; at send time, the token and the email text. | `App/Auth/PasswordReset.cs`; `Inf/Auth/PasswordReset.cs` (`PasswordResetStore`, `AccountEmailBuilder`) |
| API-AUTH-PRIVACY | Export and delete the account | Part of API-AUTH | US30: `GET /api/me/data-export` and `POST /api/me/delete-account`; anonymization in a single transaction. [ADR 0011](adr/0011-account-deletion-by-anonymization.md). | `App/Accounts/AccountPrivacy.cs`; `Inf/Identity/AccountPrivacyStore.cs`; `Domain/Accounts/DeletedAccount.cs` |
| API-AUTH-ACCOUNTS | Accounts in Identity | Part of API-AUTH | Create, authenticate, look up, know whether an account is active. | `App/Accounts/IAccountStore.cs`; `Inf/Identity/AccountStore.cs`, `FallbackPasswordHash.cs`, `LoginLockout.cs` |
| API-NOTIF-OUTBOX | Put in the outbox | Part of API-NOTIF | Attaches the booking messages to the caller's `IAppDbContext`. | `App/Notifications/BookingEmailOutbox.cs`, `OutboxMessage.cs` |
| API-NOTIF-DISPATCH | Worker loop | Part of API-NOTIF (runs in C-WORKER) | `BackgroundService`: one scope per pass, waits on `TimeProvider`. | `Inf/Notifications/OutboxDispatcher.cs` |
| API-NOTIF-PROC | Process the outbox | Part of API-NOTIF (runs in C-WORKER) | Claims the due messages (`SKIP LOCKED`), composes, sends and saves the result. | `Inf/Notifications/OutboxProcessor.cs` |
| API-NOTIF-COMPOSE | Compose the emails | Part of API-NOTIF | Text and HTML in pt-PT of the booking and account emails. | `App/Notifications/BookingEmails.cs`, `BookingNotifications.cs`, `AccountEmails.cs` |
| API-NOTIF-SENDER | Send over SMTP | Part of API-NOTIF | `IEmailSender` with MailKit, one connection per message. | `App/Notifications/BookingEmails.cs` (`IEmailSender`); `Inf/Notifications/SmtpEmailSender.cs` |

### Cross-cutting API pieces

| Id | Name | Type | Responsibility | Where it appears in the code |
| --- | --- | --- | --- | --- |
| CC-PROBLEM | Errors as ProblemDetails | Cross-cutting | Expected failures as `Result`/`ServiceError` with a stable `code`; RFC 9457 responses with `code` and `traceId`; unhandled exceptions → 500. | `App/Common/Result.cs`, `ErrorCodes.cs`, `ValidationResultExtensions.cs`; `Api/ErrorHandling/` |
| CC-VALID | Validation | Cross-cutting | Request rules with FluentValidation, pt-PT messages per field. | `App/**/*Validator*`, `*Request.cs` |
| CC-COOKIES | Session cookies and authentication | Cross-cutting | Access JWT (15 min) and refresh token (7 days) in httpOnly cookies; JwtBearer reads the `access_token` cookie. | `Api/Auth/AuthCookies.cs`, `AuthenticationSetup.cs`; `Inf/Auth/SessionService.cs`, `JwtOptions.cs`, `SessionClaims.cs` |
| CC-AUTHZ | Authorization | Cross-cutting | Fallback policy (requires a session), one policy per profile, and per owner (T02: `OwnedClubs`, `OwnedPitches`, `OwnedBookings`). | `Api/Auth/AuthenticationSetup.cs`, `HttpCurrentUser.cs`; `App/Common/ICurrentUser.cs`; `App/Clubs/OwnedClubs.cs`; `App/Pitches/OwnedPitches.cs` |
| CC-ORIGIN | Origin check | Cross-cutting (middleware) | POST/PUT/PATCH/DELETE only with an accepted `Origin`; otherwise 403 `origin_not_allowed`. | `Api/Security/OriginCheckMiddleware.cs`, `OriginCheckOptions.cs`, `SecuritySetup.cs` |
| CC-HEADERS | Response security headers | Cross-cutting (middleware) | `X-Content-Type-Options: nosniff` on every API response, without the `Server` header; Swagger only exists in Development. | `Api/Security/SecuritySetup.cs` (`UseResponseSecurityHeaders`), `Api/Program.cs` (`AddServerHeader = false`) |
| CC-RATE | Rate limiting and client IP | Cross-cutting (middleware) | Per-IP limits (IPv6 per /64) on the authentication routes and the public read ones; `X-Forwarded-For` only from configured proxies. | `Api/Security/RateLimitingSetup.cs`, `AuthRateLimitOptions.cs`, `PublicRateLimitOptions.cs`, `ClientPartition.cs`, `TrustedProxyOptions.cs` |
| CC-TIME | Injected clock | Cross-cutting | All current time comes from `TimeProvider` (`TimeProvider.System` at run time, fake in the tests). | `Inf/DependencyInjection.cs` (`AddSingleton(TimeProvider.System)`) |
| CC-CLUBTIME | Club time and slots | Cross-cutting (domain) | Local↔UTC conversion with the clock-change rules, slot generation by real elapsed time, booking window. | `Dom/Scheduling/` (`ClubTime`, `SlotGenerator`, `OpeningHours`, `TimeSlot`, `SlotDuration`, `BookingWindow`) |
| CC-DAYCAL | A day's slots and «free» | Cross-cutting (application) | The single calculation of a day's slots with blocks and bookings and the single definition of «free»; shared by the owner's preview, the public slots and the search. | `App/Pitches/PitchDayCalendar.cs`, `BookingDateInput.cs` |
| CC-SLOTCHECK | Check of a bookable slot | Cross-cutting (application) | The rules for booking a slot (active pitch, window, grid, started, blocks), shared by the player's, the owner's and the series' bookings. | `App/Bookings/BookingSlotCheck.cs` |
| CC-LOCK | Pitch lock | Cross-cutting | Transaction with `SELECT … FOR UPDATE` on the pitch's row, which serializes bookings, blocks, cancellations, no-shows and price bands. | `App/Pitches/IPitchLock.cs`; `Inf/Pitches/PitchLock.cs` |
| CC-DATAPROT | Data Protection keys | Cross-cutting | Keys that protect the password recovery tokens, stored in the database with a fixed application name. | `Inf/DependencyInjection.cs` (`PersistKeysToDbContext`); `Inf/Persistence/AppDbContext.cs` |
| CC-OPENAPI | OpenAPI document | Cross-cutting | Generated at build time (`openapi/sportsbooking-api.json`), Swagger UI in development, source of the front end types. | `Api/openapi/`, `Api/ErrorHandling/ProblemDetailsSchemaTransformer.cs`; `Web/lib/api/schema.d.ts` |
| CC-HEALTH | Health check | Cross-cutting | `GET /health`, liveness only: answers 200 if the process is alive and does not touch the database (`AddHealthChecks()` with no check registered). | `Api/Program.cs` (`AddHealthChecks`, `MapHealthChecks`) |

## 5. Front end components

The interface is in Portuguese (pt-PT), so on-screen texts are quoted in Portuguese, followed by their English meaning in parentheses.

| Id | Name | Type | Responsibility | Where it appears in the code |
| --- | --- | --- | --- | --- |
| WEB-AREA-PLAYER | Player area | Route group | Search (`/`), club page (`/clubs/[slug]`) and `As minhas reservas` (My bookings) (`/bookings`, players only). | `Web/app/(player)/` |
| WEB-AREA-MANAGE | Owner area | Route group | Clubs, pitches, calendar, statistics (`/manage/...`). | `Web/app/manage/` |
| WEB-AREA-ADMIN | Admin area | Route group | Dashboard (`/admin`) and an owner's page (`/admin/owners/[id]`). | `Web/app/admin/` |
| WEB-AREA-ACCOUNT | Account pages | Route group | Login, player and owner registration, requesting and setting a new password. | `Web/app/login/`, `register/`, `register/owner/`, `forgot-password/`, `reset-password/` |
| WEB-F-ACCOUNT | Feature: my account | Feature | The `/account` page, section `Privacidade e dados` (Privacy and data): exporting the data and deleting the account (US30). | `Web/features/account/`, `Web/components/account-guard.tsx` |
| WEB-F-AUTH | Feature: accounts | Feature | Login, registration and recovery forms; session state in the header; `useCurrentUser`. | `Web/features/auth/` |
| WEB-F-CATALOG | Feature: catalogue | Feature | Search in the address, results, club details. | `Web/features/catalog/` |
| WEB-F-BOOKING | Feature: booking | Feature | Choice in the address (`?pitch=&date=&slot=&weeks=`), slot list, summary, confirmation, weekly series. | `Web/features/booking/` |
| WEB-F-MYBOOK | Feature: my bookings | Feature | Tabs and pages in the address, cancelling with confirmation, series `2 de 4` (2 of 4). | `Web/features/my-bookings/` |
| WEB-F-CLUBS | Feature: owner's clubs | Feature | Club list, club form (includes the new-booking notice). | `Web/features/clubs/` |
| WEB-F-PITCHES | Feature: pitches | Feature | Pitches, schedule, preview, blocks and price bands (one panel at a time). | `Web/features/pitches/` |
| WEB-F-CALENDAR | Feature: calendar | Feature | Daily and weekly view (real-time layout), details, cancelling, new booking for a customer, no-shows. | `Web/features/calendar/` |
| WEB-F-STATS | Feature: statistics | Feature | Owner's statistics per club and per pitch, period filter. | `Web/features/statistics/`, `Web/components/period-filter.tsx` |
| WEB-F-ADMIN | Feature: admin | Feature | Cards, charts (Recharts), owners list, top 10, owner page and deactivate/reactivate. | `Web/features/admin/` |
| WEB-API-CLIENT | API client (browser) | Library | The only door to `/api/*` in the browser: ProblemDetails → `ApiError`, one shared refresh on 401 (Web Lock between tabs), messages by code. | `Web/lib/api/` (`client.ts`, `error-messages.ts`, `form-errors.ts`, `schema.d.ts`) |
| WEB-SERVER | Server-side API calls | Library (`server-only`) | Session (`/api/auth/me` with the user's cookie), the club's public page (no cookies), admin totals; internal API URL. | `Web/lib/server/` (`session.ts`, `public-club.ts`, `admin-stats.ts`, `api-url.ts`) |
| WEB-GUARDS | Area guards | Components | Redirect by profile on the server (a convenience; the API always decides). | `Web/components/area-guard.tsx`, `player-guard.tsx`, `account-guard.tsx`, `guest-only.tsx`; `Web/lib/auth/redirects.ts`, `request-path.ts` |
| WEB-PROXY | `proxy.ts` | Next.js middleware | On `/manage/*`, `/admin/*` and `/bookings`, writes the requested path to the `x-pathname` header for the return after login, and answers 404 to an impossible id in those areas and to a public club (`/clubs/{slug}`) that the API does not have. Does not renew sessions. | `Web/proxy.ts`, `Web/lib/not-found-check.ts` |
| WEB-REWRITE | Rewrites and headers | Next.js configuration | Forwards `/api/*` to the internal API; applies WEB-HEADERS' headers to all pages and `/reset-password`'s own (`no-referrer`, `no-store`, which win over the general ones); no `X-Powered-By`. | `frontend/next.config.ts` |
| WEB-HEADERS | Site security headers | Module (pure functions) | `Content-Security-Policy` (own origin only; `'unsafe-inline'` in scripts and styles, a known limit), `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, and HSTS with `upgrade-insecure-requests` only with `ENABLE_HSTS=true` at build time, outside development. Zod runs without the compiled parser (`zod-config.ts`) so as not to probe `eval` under the policy. | `frontend/src/lib/security-headers.ts`, `frontend/src/lib/zod-config.ts` |
| WEB-UX | UX and accessibility foundations | Shared front end modules | A single focus-ring rule and contrast tokens (`globals.css`); focus after actions (`useFocusOn`, `useRestoreFocus`) and `ResultNotice`; the guard for unexpected errors (`isExpectedApiError`, `unexpectedErrorMessage`); page frame (`PublicHeader`, `Main`, `SkipLink`, `PAGE_WIDTH`, 404 and error pages); `Skeleton`, `LoadingRegion`, `EmptyState`; for the player's flow, `Button size="cta"`, `BookingActions` (fixed bar on mobile), `useRetryAfter` and `RateLimitMessage` (wait time after a 429), the `Conta` (Account) menu of `AccountStatus` and `formatLisbonDayHeading`; for the owner area, the shared `AccountMenu`, `ScrollRegion`, `PAGE_WIDTH.calendar` and `StatisticsTable`; for the admin, `ChartDataTable` and `useDocumentTitle`. | `frontend/src/lib/focus.ts`, `frontend/src/lib/document-title.ts`, `frontend/src/lib/api/expected-error.ts`, `frontend/src/components/` (`account-menu`, `scroll-region`, `result-notice`, `public-header`, `main`, `skip-link`, `page-width`, `loading-region`, `empty-state`, `ui/skeleton`), `frontend/src/app/` (`globals.css`, `not-found.tsx`, `error.tsx`, `global-error.tsx`) |
| WEB-DATETIME | Dates in Lisbon time | Library | All of the front end's date formatting and arithmetic, with the API's clock-change rules. | `Web/lib/datetime.ts` |
| WEB-UI | Shared components | Library | shadcn/ui, form fields, area headers, route errors and loading, providers (TanStack Query, toasts). | `Web/components/`, `Web/components/ui/` |

## 6. Domain entities and invariants

| Id | Name | Type | Main invariants | Where it appears in the code |
| --- | --- | --- | --- | --- |
| D-CLUB | Club | Entity (Domain) | A single owner (`OwnerId`); unique `Slug`, generated from the name at creation and never changed; `LocalityKey` follows the locality; cancellation window from 1 to 168 h (24 by default); `Europe/Lisbon` time zone. | `Dom/Clubs/Club.cs`, `ClubRules.cs`, `ClubSlug.cs` |
| D-PITCH | Pitch | Entity (Domain) | Never deleted (active/inactive); base price > 0 and ≤ 500, two decimal places; name unique in the club (`NameKey`); slot of 30, 60 or 90 min; slots only come from `SlotsOn` → `SlotGenerator`; a slot's price only comes from `PriceForSlot`. | `Dom/Pitches/Pitch.cs`, `PitchRules.cs`, `PitchScheduleRules.cs`, `Sport.cs` |
| D-OPENDAY | Opening day | Dependent entity (Domain) | At most one interval per weekday; hours in 30-minute steps; closing `00:00` = midnight, never after midnight. | `Dom/Pitches/PitchOpeningDay.cs`, `Dom/Scheduling/OpeningHours.cs` |
| D-PRICEBAND | Price band | Dependent entity (Domain) | At least one day; start and end in 30-minute steps, end `00:00` = midnight; never overlaps another on the same pitch on a shared day (touching is allowed); at most 20 per pitch. | `Dom/Pitches/PitchPriceBand.cs`, `PriceBandRules.cs` |
| D-BLOCK | Block | Entity (Domain) | `[StartsAt, EndsAt)` in UTC with the end after the start; optional reason up to 200; can only be removed before it starts. | `Dom/Pitches/Block.cs` |
| D-BOOKING | Booking | Entity (Domain) | From a player **or** from a customer, never both and never neither; never deleted, changes state (`Confirmed`, `CancelledByPlayer`, `CancelledByClub`, `NoShow`) through methods that return the result; stores the price at the time; optional weekly series (`SeriesId`). | `Dom/Bookings/Booking.cs` |
| D-SLOT | Slot | Value (Domain) | `[start, end)` interval in UTC of exact duration; "started" when the start ≤ now. | `Dom/Scheduling/TimeSlot.cs`, `SlotDuration.cs` |
| D-POLICIES | Account rules | Values (Domain) | Email, name, phone and password (≥ 8 characters); accepted Terms version (up to 32 characters); profiles `Player`, `Owner`, `Admin`. | `Dom/Accounts/` |
| D-USER | Account | Entity (Infrastructure, Identity) | One email = one account = one profile; unique normalized email; `RegisteredAt`; deactivated when `DeactivatedAt` is set; `AcceptedTermsVersion` and `TermsAcceptedAt` set at registration (T07), empty on earlier accounts and the seeds'. | `Inf/Identity/AppUser.cs` |
| D-REFRESH | Refresh token | Entity (Infrastructure) | Only the SHA-256 is stored; atomic conditional rotation; absolute lifetime; reuse revokes all the account's sessions. | `Inf/Auth/RefreshToken.cs` |
| D-OUTBOX | Outbox message | Entity (Application) | Type and payload without addresses; a single outcome (sent, skipped or failed); retries with a doubling interval. | `App/Notifications/OutboxMessage.cs` |
| D-STATUSCHG | An owner's status change | Entity (Infrastructure) | Append-only (never changed or deleted); action, reason and the admin who made it. | `Inf/Admin/OwnerStatusChange.cs` |

## 7. Database objects

| Id | Name | Type | What for | Where it appears in the code |
| --- | --- | --- | --- | --- |
| DB-USERS | `users`, `roles`, `user_roles` (and `user_claims`, `user_logins`, `user_tokens`, `role_claims`) | Tables (Identity) | Accounts, profiles and Identity data; `ix_users_normalized_email` (unique), `ix_users_registered_at`; optional `accepted_terms_version` and `terms_accepted_at`; `deleted_at` (US30), optional, on a deleted and anonymized account. | `Inf/Identity/IdentityModelConfiguration.cs`; migrations `AddIdentity`, `AddAccountDetailsAndSessions`, `AddUserRegisteredAt`, `AddTermsAcceptance` |
| DB-REFRESH | `refresh_tokens` | Table | Hashes of the refresh tokens, `ix_refresh_tokens_token_hash`. | `Inf/Auth/RefreshToken.cs` |
| DB-CLUBS | `clubs` | Table | Clubs; `ix_clubs_slug`, `ix_clubs_locality_key`, `ix_clubs_owner_id_name`, `ck_clubs_cancellation_window_hours`. | `Inf/Clubs/ClubConfiguration.cs` |
| DB-PITCHES | `pitches`, `pitch_opening_hours`, `pitch_price_bands` | Tables | Pitches (`ix_pitches_club_id_name_key` unique, `ck_pitches_*`), weekly schedule and price bands (`ck_pitch_price_bands_*`). | `Inf/Pitches/PitchConfiguration.cs`, `PitchPriceBandConfiguration.cs` |
| DB-BLOCKS | `blocks` | Table | Blocks (`ix_blocks_pitch_id_starts_at`). | `Inf/Pitches/BlockConfiguration.cs` |
| DB-BOOKINGS | `bookings` | Table | Bookings; `ck_bookings_player_or_customer`, `ck_bookings_status`, `ck_bookings_price`; indexes by pitch, player, creation and series. | `Inf/Bookings/BookingConfiguration.cs` |
| DB-NOOVERLAP | `bookings_no_overlap` | Exclusion constraint (GiST) | No confirmed booking overlaps another on the same pitch: `pitch_id WITH =`, `tstzrange(starts_at, ends_at) WITH &&`, `WHERE status = 'Confirmed'`. Violation = SQLSTATE 23P01 → 409 `slot_taken`. | Migration `AddBookings` (hand-written SQL) |
| DB-GIST | `btree_gist` | Extension | Needed for the `=` on `pitch_id` in the GiST index. | Migration `AddBookings` |
| DB-OUTBOX | `outbox_messages` | Table | Emails waiting to be sent and their outcome; `ix_outbox_messages_pending_next_attempt_at`, `ck_outbox_messages_one_outcome`. | `Inf/Notifications/OutboxMessageConfiguration.cs` |
| DB-OUTBOXUQ | `ux_outbox_messages_booking_notification` | Partial unique index | At most one reminder and one owner notice per booking (`type`, `payload ->> 'bookingId'`). | Migration `AddNoShowNotificationsAndReminders` (hand-written SQL) |
| DB-STATUSCHG | `owner_status_changes` | Table | History of owner deactivations and reactivations; `RESTRICT` foreign keys. | `Inf/Admin/OwnerStatusChange.cs` |
| DB-AUDITTRG | `owner_status_changes_append_only` | Function and triggers (`owner_status_changes_no_update_delete`, `owner_status_changes_no_truncate`) | Refuses `UPDATE`, `DELETE` and `TRUNCATE` on the history (SQLSTATE 23001). | Migration `AddOwnerStatusChanges` (hand-written SQL) |
| DB-DPKEYS | `data_protection_keys` | Table | Data Protection keys (CC-DATAPROT). | Migration `AddDataProtectionKeys` |
| DB-MIGHIST | `__EFMigrationsHistory` | Table (EF Core) | Applied migrations. | `Inf/Persistence/Migrations/` |

## 8. Production (T06)

These ids only appear in the production deployment views, which has existed since 2026-11-09. The "not adopted" ids stay here so that older documents keep making sense.

| Id | Name | Type | What is planned | Source |
| --- | --- | --- | --- | --- |
| PL-CADDY | Caddy | Container (reverse proxy) | **Not adopted** (ADR 0009). It was to provide HTTPS and put the front end and the API on the same origin; that role belongs to the Next.js rewrite. | `docs/stack.md` (Deploy) |
| PL-HOST | Production machine | Deployment node | **Not adopted** (ADR 0009). A virtual machine with docker compose. | `docs/stack.md` (Deploy) |
| PL-GHCR | GitHub Container Registry | External system | **Not adopted** (ADR 0009): Render builds the image from the repository. | `docs/stack.md` (Deploy) |
| PL-DEPLOY | Deploy workflow | Pipeline | `.github/workflows/deploy.yml`: after green CI on `main`, applies the migrations, calls Render's deploy hook, publishes the site with the Vercel CLI and runs a smoke test. | `.github/workflows/deploy.yml`, [ADR 0009](adr/0009-deployment-target.md), [physical view](4plus1/physical.md) |
| PL-MIGBUNDLE | Migration bundle | Artifact | `dotnet ef migrations bundle`, built and applied in GitHub Actions as the schema owner (the API connects with a user without DDL). | `.github/workflows/deploy.yml`, `prod-data.yml` |
| PL-DBUSERS | Two database users | Configuration | `postgres`, schema owner (migrations, commands, `btree_gist`), and `sportsbooking_app`, with read and write only (the API). | `docs/deployment/supabase-roles.sql` |
| PL-BREVO | Brevo | External system (SMTP) | The production SMTP server (X-SMTP in production), with a sender verified by email. | `docs/deployment/environment-variables.md`, `docs/backlog.md` (US22, T06) |
| PL-PGMANAGED | Managed PostgreSQL | External system (alternative) | **Not adopted** as a separate alternative: the managed PostgreSQL is PL-SUPABASE. | `docs/stack.md` (Deploy) |
| PL-BACKUP | Daily backup | Task | **Not adopted** in this demo: Supabase's free plan does not include automatic backups and the data is demo data. | `docs/stack.md` (Deploy), [ADR 0009](adr/0009-deployment-target.md), [physical view](4plus1/physical.md) |
| PL-RENDER | Render | External system (hosting platform) | Hosts the API in production, on the free plan. | [ADR 0009](adr/0009-deployment-target.md), [physical view](4plus1/physical.md) |
| PL-RENDER-WEB | Render's public web service | Deployment node | **Not adopted** (ADR 0009): the site runs on Vercel (PL-VERCEL). | [ADR 0009](adr/0009-deployment-target.md), [physical view](4plus1/physical.md) |
| PL-RENDER-API | API's public web service on Render | Deployment node | Runs C-API with C-WORKER, in Docker, with a public URL. Sleeps without traffic and automatic deploy is turned off. (In the first version of ADR 0009 it was a private service.) | [ADR 0009](adr/0009-deployment-target.md), [physical view](4plus1/physical.md) |
| PL-SUPABASE | Supabase PostgreSQL | External system (managed PostgreSQL) | Hosts the database (C-DB) in production, on the free plan. | [ADR 0009](adr/0009-deployment-target.md), [physical view](4plus1/physical.md) |
| PL-RENDER-EDGE | Render edge | Managed infrastructure (ingress proxy) | Cloudflare and Render's load balancers: terminate the TLS of `*.onrender.com` and forward to PL-RENDER-API. The `X-Forwarded-For` chain is not documented. | [ADR 0009](adr/0009-deployment-target.md), [physical view](4plus1/physical.md) |
| PL-RENDER-NET | Render private network | Network | **Not adopted** (ADR 0009): Vercel reaches the API over the Internet. | [ADR 0009](adr/0009-deployment-target.md), [physical view](4plus1/physical.md) |
| PL-SUPAPOOL | Supabase connection pooler | Managed infrastructure | Supavisor in session mode (port 5432, IPv4), used by the API and GitHub Actions; transaction mode and the direct host are excluded. | [ADR 0009](adr/0009-deployment-target.md), [physical view](4plus1/physical.md) |
| PL-DNS | Own domain's DNS | External system | **Not adopted** (ADR 0009): without a domain of our own, the `*.vercel.app` and `*.onrender.com` addresses are used. | [ADR 0009](adr/0009-deployment-target.md), [physical view](4plus1/physical.md) |
| PL-DEPENDABOT | Dependabot | Configuration | `.github/dependabot.yml` for NuGet, npm, GitHub Actions and the back end and front end `Dockerfile`s. **Done** before T06 (the id stays); Docker was added in `chore/repo-hygiene`. | `.github/dependabot.yml`, `docs/backlog.md` (T06), `docs/stack.md` (security checklist) |
| PL-SECHEADERS | Security headers | Configuration and code | Done before T06 (the id stays): see CC-HEADERS and WEB-HEADERS. HSTS is turned on with `ENABLE_HSTS=true` on Vercel. | `docs/backlog.md` (T06), `docs/stack.md` (security checklist) |
| PL-VERCEL | Vercel | External system (hosting platform) | Hosts C-WEB in production, on the Hobby plan; it is the only origin the browser knows. | [ADR 0009](adr/0009-deployment-target.md), [physical view](4plus1/physical.md) |
| PL-VERCEL-EDGE | Vercel edge | Managed infrastructure (ingress proxy) | HTTPS on `*.vercel.app`, and the `/api/*` rewrite to the API, fixed at build time. Writes `X-Forwarded-For` with the visitor's IP. | [ADR 0009](adr/0009-deployment-target.md), [physical view](4plus1/physical.md) |
| PL-DEPLOYHOOK | Render deploy hook | Configuration | Secret URL that launches the deploy of a commit on the API; called by PL-DEPLOY, with automatic deploy turned off. | `.github/workflows/deploy.yml`, `docs/deployment/runbook.md` |
| PL-CODEQL | CodeQL workflow | Pipeline | `.github/workflows/codeql.yml`: code analysis for C# and JavaScript/TypeScript (`build-mode: none`) on `pull_request`, on `push` to `main` and on Mondays. Independent of CI and the deploy. | `.github/workflows/codeql.yml`, `docs/backlog.md` (T06) |
| PL-E2E | E2E workflow | Pipeline | `.github/workflows/e2e.yml`, on `pull_request` and on `push` to `main`: builds and brings up the `docker compose` stack with the demo data and runs the Playwright tests in `e2e/`; uploads the report, traces and videos if it fails. Independent of CI and the deploy. | `.github/workflows/e2e.yml`, [ADR 0010](adr/0010-playwright-end-to-end-tests.md) |
| PL-KEEPALIVE | Keepalive workflow | Pipeline | `.github/workflows/keepalive.yml`, on Mondays and Thursdays and by hand, only with `DEPLOY_ENABLED=true`: a `GET /api/localities` through the public site (a read query against the database, which also wakes the API) so that Supabase does not pause the project for inactivity. No secrets; uses the `SITE_URL` variable. | `.github/workflows/keepalive.yml`, `docs/deployment/runbook.md`, [ADR 0009](adr/0009-deployment-target.md) |
| PL-PRODDATA | Production data workflow | Pipeline | `.github/workflows/prod-data.yml`, manual: `migrate-only`, `set-admin-password` and `seed-demo` against Supabase, because Render's free plan has no shell. | `.github/workflows/prod-data.yml`, `docs/deployment/runbook.md` |

**Note (production plan).** `docs/stack.md` describes a virtual machine with Caddy. The production target (ADR 0009, accepted on 2026-11-09) is Vercel, free Render with the API public, Supabase and Brevo: Next.js (C-WEB) remains the reverse proxy. The ADR says which parts of the Stack it replaces.

## 9. Development view

Elements that only the [development view](4plus1/development.md) uses: repository folders, test projects, generated files and CI jobs. The back end projects are the `L-` layers and the front end folders reuse the `WEB-` ids.

| Id | Name | Type | Responsibility | Where it appears in the code |
| --- | --- | --- | --- | --- |
| DEV-BACKEND | `backend/` | Folder | .NET solution: four projects in `src/`, two test ones in `tests/`, central package versions, `Dockerfile`. | `backend/` |
| DEV-FRONTEND | `frontend/` | Folder | Next.js application (`src/`), configuration, `Dockerfile`. | `frontend/` |
| DEV-DOCS | `docs/` | Folder | `backlog.md`, `stack.md` and the architecture. | `docs/` |
| DEV-ARCHDOCS | `docs/architecture/` | Folder | These documents, the ADRs, the diagrams (`.puml` in `c4/` and `4plus1/`, `.svg` in `out/`), the common style (`style.puml`), the typeface (`fonts/`) and the scripts that render them. | `docs/architecture/` |
| DEV-SCRIPTS | `scripts/` | Folder | Check of the links, the gallery and the diagram aliases; generation of the site's icons. | `scripts/check-architecture-docs.sh`, `scripts/check-architecture-docs.ps1`, `scripts/generate-icons.mjs` |
| DEV-WORKFLOWS | `.github/workflows/` | Folder | The workflows: CI, CodeQL, E2E, deploy, production data and keepalive. | `.github/workflows/` |
| DEV-E2E | `e2e/` | Folder | End-to-end tests: Playwright (Chromium), four flows, `scripts/stack.mjs` which brings up the `docker compose` stack with the demo data, and `compose.e2e.yml`. Does not depend on front end or API code. | `e2e/`, [ADR 0010](adr/0010-playwright-end-to-end-tests.md) |
| DEV-COMPOSE | `docker-compose.yml` | File | Local stack: `postgres`, `mailpit`, `api`, `frontend`. | `docker-compose.yml`, `.env.example` |
| DEV-ROOTCFG | Root configuration | Files | Pinned SDK, code style, line endings, ignored files, README and AGENTS. | `global.json`, `.editorconfig`, `.gitattributes`, `.gitignore`, `README.md`, `AGENTS.md` |
| DEV-TEST-UNIT | Back end unit tests | Test project | Domain, services and API pieces without a database (xUnit v3, Shouldly, `FakeTimeProvider`). | `backend/tests/SportsBooking.UnitTests/` |
| DEV-TEST-INT | Back end integration tests | Test project | Complete endpoints against a real PostgreSQL (Testcontainers), with `ApiFactory`. | `backend/tests/SportsBooking.IntegrationTests/` |
| DEV-TEST-WEB | Front end tests | Tests | Vitest, Testing Library and MSW; the `*.test.ts(x)` files sit next to the code, the support in `src/test/`. | `Web/test/`, `frontend/vitest.config.mts`, `frontend/vitest.setup.ts` |
| DEV-WEBTYPES | Generated API types | Generated file | TypeScript types generated from the OpenAPI document (CC-OPENAPI) by `openapi-typescript`; never edited by hand. | `Web/lib/api/schema.d.ts` |
| DEV-CI-BACKEND | `backend` job | CI job | "Backend (build + tests)". | `.github/workflows/ci.yml` |
| DEV-CI-FRONTEND | `frontend` job | CI job | "Frontend (lint, typecheck, tests, build)". | `.github/workflows/ci.yml` |
| DEV-CI-DIAGRAMS | `diagrams` job | CI job | "Docs (architecture diagrams)": renders (with the PlantUML image pinned in `style.puml`) and compares the SVGs, and runs `scripts/check-architecture-docs.sh` (links, gallery and aliases). | `.github/workflows/ci.yml` |

## 10. Physical view (today's deployment)

Nodes and infrastructure of the environment that exists today, used by the [physical view](4plus1/physical.md). The production ones are `PL-` (section 8).

| Id | Name | Type | Responsibility | Where it appears in the code |
| --- | --- | --- | --- | --- |
| DEP-DEVHOST | Development machine | Deployment node | Runs Docker (Engine or Desktop), the browser and, optionally, the API and the front end outside Docker. | `README.md`, `docker-compose.yml` |
| DEP-COMPOSE-NET | Compose project `sportsbooking` and its network | Deployment node (Docker bridge network) | Connects `postgres`, `mailpit`, `api` and `frontend`, which call each other by service name; publishes ports 3000, 5080, 5432, 1025 and 8025 on the host. | `docker-compose.yml` (`name: sportsbooking`) |
| DEP-PGVOLUME | `postgres-data` volume | Docker volume | Stores PostgreSQL's files in `/var/lib/postgresql`; survives `docker compose down`, deleted with `down -v`. | `docker-compose.yml` (`volumes`) |

## 11. Scenarios

The use cases of the [scenarios view](4plus1/scenarios.md). Each links elements and diagrams of the other views; there are no new elements in the diagrams.

| Id | Name | Type | Actors | Where it appears |
| --- | --- | --- | --- | --- |
| SC-01 | Visitor searches for and books a slot | Scenario | P-VIS, P-JOG | US16, US17, US18; [4plus1/scenarios.md](4plus1/scenarios.md) |
| SC-02 | Owner sets up club, pitches, schedules and prices | Scenario | P-DONO | US02, US06 to US10; same |
| SC-03 | Owner cancels a booking in the calendar | Scenario | P-DONO, P-JOG | US11, US12, US22; same |
| SC-04 | Weekly booking with one date skipped | Scenario | P-JOG | US20; same |
| SC-05 | Password recovery | Scenario | P-VIS (with an account) | US05; same |
| SC-06 | Admin deactivates an owner | Scenario | P-ADMIN, P-DONO | US29; same |
| SC-07 | Race for the same slot | Scenario | P-JOG | T03, US18; same |

## 12. Names and aliases in the diagrams

The diagrams draw only names. The id stays in the PlantUML alias: the id with `_` (`C-API` → `C_API`) and, when several classes belong to the same element, the id followed by the class name (`D_BOOKING_BookingStatus`). An element can have shorter names in some diagrams; this table links each alias to the id, to the drawn names and to the diagrams where it appears, and the checker (`scripts/check-architecture-docs`) confirms it in both directions.

Id `—`: groupings that only exist in the drawing, with no element of their own in the inventory (`FEATURES` groups the nine `WEB-F-*` features). The boxes of the sequence diagrams (`box`) have no alias in PlantUML: ASP.NET Core API = C-API, Next.js = C-WEB, Outbox worker = C-WORKER.

| Alias | Id | Drawn name | Diagrams |
| --- | --- | --- | --- |
| `ADDACC` | L-INF | AddAccounts · Identity, IAccountStore, admin seed, Data Protection, PasswordHasherOptions | development-di |
| `ADDAPI` | L-API | The API's own extensions | development-di |
| `ADDAPP` | L-APP | AddApplication() | development-di |
| `ADDINF` | L-INF | AddInfrastructure(config) | development-di |
| `ADDMAIL` | L-INF | AddEmail · EmailOptions, OutboxOptions, IEmailSender, OutboxProcessor, OutboxDispatcher (HostedService) | development-di |
| `ADDSES` | L-INF | AddSessions · JwtOptions, ISessionService, PublicSiteOptions, IPasswordResetStore, AccountEmailBuilder | development-di |
| `API_ADMIN` | API-ADMIN | Administration; Owner status | process-owner-deactivation, 03-components-api-accounts, 03-components-api-overview |
| `API_ADMINPW` | API-ADMINPW | set-admin-password | 03-components-api-accounts |
| `API_ADMINSEED` | API-ADMINSEED | Admin seed | 03-components-api-accounts |
| `API_AUTH` | API-AUTH | AuthController.Me; Accounts and sessions; Accounts and sessions (AuthController, api/auth; MeAccountController, api/me); Accounts | process-guard, process-login, process-refresh, process-reset-confirm, process-reset-request, 03-components-api-accounts, 03-components-api-bookings, 03-components-api-overview |
| `API_AUTH_ACCOUNTS` | API-AUTH-ACCOUNTS | Accounts; Accounts in Identity | process-book, process-login, process-series, 03-components-api-accounts |
| `API_AUTH_LOGIN` | API-AUTH-LOGIN | Login | process-login, 03-components-api-accounts |
| `API_AUTH_REG` | API-AUTH-REG | Registration | 03-components-api-accounts |
| `API_AUTH_RESET` | API-AUTH-RESET | AccountEmailBuilder; Recovery | process-outbox-skip, process-reset-confirm, process-reset-request, 03-components-api-accounts, 03-components-api-notifications |
| `API_BLOCKS` | API-BLOCKS | Blocks | 03-components-api-overview |
| `API_BOOK` | API-BOOK | Player bookings (BookingsController, MeBookingsController; Player policy); Player bookings | 03-components-api-bookings, 03-components-api-notifications, 03-components-api-overview |
| `API_BOOK_MINE` | API-BOOK-MINE | Cancel (player); Cancel; My bookings and cancelling | process-cancel-club, process-cancel-player, 03-components-api-bookings |
| `API_BOOK_SERIES` | API-BOOK-SERIES | Weekly series | process-series, 03-components-api-bookings |
| `API_BOOK_SINGLE` | API-BOOK-SINGLE | Single booking | process-book, 03-components-api-bookings |
| `API_BOOK_STORE` | API-BOOK-STORE | Booking store | process-book, process-series, 03-components-api-bookings |
| `API_CATALOG` | API-CATALOG | Visible clubs; Public catalogue | 03-components-api-bookings, 03-components-api-overview |
| `API_CLUBBOOK` | API-CLUBBOOK | Owner bookings; Owner's calendar and bookings | process-cancel-club, 03-components-api-bookings, 03-components-api-notifications, 03-components-api-overview |
| `API_CLUBS` | API-CLUBS | Owner's clubs | 03-components-api-overview |
| `API_NOTIF` | API-NOTIF | Outbox; Notifications (outbox) | 03-components-api-accounts, 03-components-api-bookings, 03-components-api-overview |
| `API_NOTIF_COMPOSE` | API-NOTIF-COMPOSE | Compose; Compose the emails | process-outbox-skip, process-outbox, 03-components-api-notifications |
| `API_NOTIF_DISPATCH` | API-NOTIF-DISPATCH | Loop; Worker loop | process-outbox, 03-components-api-notifications |
| `API_NOTIF_OUTBOX` | API-NOTIF-OUTBOX | Put in the outbox | process-book, process-cancel-club, process-cancel-player, process-series, 03-components-api-notifications |
| `API_NOTIF_PROC` | API-NOTIF-PROC | Process; Process the outbox; Process what is due | process-outbox-skip, process-outbox, process-reset-request, 03-components-api-notifications |
| `API_NOTIF_SENDER` | API-NOTIF-SENDER | Send | process-outbox-skip, process-outbox, process-reset-request, 03-components-api-notifications |
| `API_PERSIST` | API-PERSIST | Persistence | 03-components-api-overview |
| `API_PITCHES` | API-PITCHES | Pitches | 03-components-api-overview |
| `API_PRICE` | API-PRICE | Price bands | 03-components-api-overview |
| `API_SCHED` | API-SCHED | Schedules | 03-components-api-overview |
| `API_STATS` | API-STATS | Owner's statistics | 03-components-api-overview |
| `A_SRC` | — | README.md, inventory.md, style.puml, render.sh, render.ps1, c4/ and 4plus1/ (.md and .puml), out/ (.svg), fonts/, adr/ | development-repo |
| `B_API` | C-API | API process (port 8080; 5080 on the host) | 02-containers-dev |
| `B_CFG` | — | Directory.Build.props, Directory.Packages.props, dotnet-tools.json, SportsBooking.slnx, Dockerfile | development-repo |
| `B_SRC` | — | src/ | development-repo |
| `B_TESTS` | — | tests/ | development-repo |
| `CC_AUTHZ` | CC-AUTHZ | Per-owner authorization; Authorization | process-cancel-club, process-owner-deactivation, 03-components-api-bookings |
| `CC_CLUBTIME` | CC-CLUBTIME | Dom/Scheduling | logical-code-slots |
| `CC_CLUBTIME_BookingWindow` | CC-CLUBTIME | BookingWindow | logical-code-slots |
| `CC_CLUBTIME_ClubTime` | CC-CLUBTIME | ClubTime | logical-code-slots |
| `CC_CLUBTIME_OpeningHours` | CC-CLUBTIME | OpeningHours | logical-code-slots |
| `CC_CLUBTIME_SlotDuration` | CC-CLUBTIME | SlotDuration | logical-code-slots |
| `CC_CLUBTIME_SlotGenerator` | CC-CLUBTIME | SlotGenerator | logical-code-slots |
| `CC_COOKIES` | CC-COOKIES | JwtBearer; Sessions and cookies | process-guard, process-login, process-refresh, 03-components-api-accounts |
| `CC_DATAPROT` | CC-DATAPROT | Data Protection | process-reset-confirm, process-reset-request, 03-components-api-accounts, 03-components-api-notifications |
| `CC_DAYCAL` | CC-DAYCAL | PitchDayCalendar; A day's slots and «free» | logical-code-slots, 03-components-api-overview |
| `CC_DAYCAL_DaySlot` | CC-DAYCAL | DaySlot | logical-code-slots |
| `CC_DAYCAL_PitchInZone` | CC-DAYCAL | PitchInZone | logical-code-slots |
| `CC_LOCK` | CC-LOCK | Pitch lock | process-book, process-cancel-club, process-cancel-player, process-series, 03-components-api-bookings |
| `CC_SLOTCHECK` | CC-SLOTCHECK | BookingSlotCheck; Slot check | logical-code-slots, process-book, process-series, 03-components-api-bookings |
| `CC_TIME` | CC-TIME | Clock; Injected clock | process-outbox, 03-components-api-notifications |
| `COMPOSE` | DEP-COMPOSE-NET | docker compose (name: sportsbooking) | 02-containers-dev |
| `C_API` | C-API | ASP.NET Core API; API; ASP.NET Core API (requests); api service | logical-layers, physical, process-refresh-client, process-runtime, 02-containers-dev, 02-containers-prod, 03-components-api-accounts, 03-components-api-bookings, 03-components-api-notifications, 03-components-api-overview, 03-components-web, 04-deployment-dev, 04-deployment-prod |
| `C_BROWSER` | C-BROWSER | User's browser; Browser | physical, process-guard, process-refresh-client, process-runtime, 02-containers-dev, 02-containers-prod, 03-components-web, 04-deployment-dev, 04-deployment-prod |
| `C_CLI` | C-CLI | seed-demo, set-admin-password [The same executable, another process] | process-runtime |
| `C_DB` | C-DB | Database; PostgreSQL; postgres service | physical, process-book, process-cancel-club, process-cancel-player, process-login, process-outbox-skip, process-outbox, process-reset-confirm, process-reset-request, process-runtime, process-series, 02-containers-dev, 02-containers-prod, 03-components-api-accounts, 03-components-api-bookings, 03-components-api-notifications, 03-components-api-overview, 04-deployment-dev, 04-deployment-prod |
| `C_WEB` | C-WEB | Next.js application; Next.js; frontend service | physical, process-runtime, 02-containers-dev, 02-containers-prod, 03-components-api-overview, 03-components-web-features, 03-components-web, 04-deployment-dev, 04-deployment-prod |
| `C_WORKER` | C-WORKER | Email worker; Outbox worker; Email worker (BackgroundService in the API process) | physical, process-runtime, 02-containers-dev, 02-containers-prod, 03-components-api-notifications, 04-deployment-dev, 04-deployment-prod |
| `DB_BLOCKS` | DB-BLOCKS | blocks | 03-components-api-bookings |
| `DB_BOOKINGS` | DB-BOOKINGS | bookings | 03-components-api-bookings, 03-components-api-notifications |
| `DB_DPKEYS` | DB-DPKEYS | data_protection_keys | 03-components-api-accounts |
| `DB_NOOVERLAP` | DB-NOOVERLAP | bookings_no_overlap | 03-components-api-bookings |
| `DB_OUTBOX` | DB-OUTBOX | outbox_messages | 03-components-api-accounts, 03-components-api-bookings, 03-components-api-notifications |
| `DB_OUTBOXUQ` | DB-OUTBOXUQ | ux_outbox_messages_booking_notification | 03-components-api-notifications |
| `DB_PITCHES` | DB-PITCHES | pitches | 03-components-api-bookings |
| `DB_REFRESH` | DB-REFRESH | refresh_tokens | process-owner-deactivation, process-refresh, 03-components-api-accounts |
| `DB_STATUSCHG` | DB-STATUSCHG | owner_status_changes | process-owner-deactivation, 03-components-api-accounts |
| `DB_USERS` | DB-USERS | users; users, user_roles; users, roles, user_roles | process-owner-deactivation, process-refresh, 03-components-api-accounts, 03-components-api-notifications |
| `DEP_COMPOSE_NET` | DEP-COMPOSE-NET | Compose project «sportsbooking», default network | 04-deployment-dev |
| `DEP_DEVHOST` | DEP-DEVHOST | Development machine | 04-deployment-dev |
| `DEP_PGVOLUME` | DEP-PGVOLUME | postgres-data volume | 04-deployment-dev |
| `DEV_ARCHDOCS` | DEV-ARCHDOCS | architecture/ | development-repo |
| `DEV_BACKEND` | DEV-BACKEND | backend/ | development-repo |
| `DEV_COMPOSE` | DEV-COMPOSE | docker-compose.yml | development-repo |
| `DEV_DOCS` | DEV-DOCS | docs/ | development-repo |
| `DEV_FRONTEND` | DEV-FRONTEND | frontend/ | development-repo |
| `DEV_ROOTCFG` | DEV-ROOTCFG | Root configuration | development-repo |
| `DEV_SCRIPTS` | DEV-SCRIPTS | scripts/ | development-repo |
| `DEV_TEST_INT` | DEV-TEST-INT | SportsBooking.IntegrationTests | development-backend |
| `DEV_TEST_UNIT` | DEV-TEST-UNIT | SportsBooking.UnitTests | development-backend |
| `DEV_TEST_WEB` | DEV-TEST-WEB | Front end tests | development-frontend |
| `DEV_WEBTYPES` | DEV-WEBTYPES | Generated API types | development-frontend |
| `DEV_WORKFLOWS` | DEV-WORKFLOWS | .github/workflows/ | development-repo |
| `D_BLOCK` | D-BLOCK | Block | logical-domain-booking |
| `D_BOOKING` | D-BOOKING | Booking | logical-code-booking, logical-domain-accounts, logical-domain-booking |
| `D_BOOKING_BookingRules` | D-BOOKING | BookingRules | logical-code-booking |
| `D_BOOKING_BookingStatus` | D-BOOKING | BookingStatus | logical-code-booking, logical-domain-booking |
| `D_BOOKING_BookingStatuses` | D-BOOKING | BookingStatuses | logical-code-booking |
| `D_BOOKING_CancellationOutcome` | D-BOOKING | CancellationOutcome | logical-code-booking |
| `D_BOOKING_ClubCancellationOutcome` | D-BOOKING | ClubCancellationOutcome | logical-code-booking |
| `D_BOOKING_NoShowOutcome` | D-BOOKING | NoShowOutcome | logical-code-booking |
| `D_CLUB` | D-CLUB | Club | logical-domain-booking |
| `D_OPENDAY` | D-OPENDAY | PitchOpeningDay | logical-domain-booking |
| `D_OUTBOX` | D-OUTBOX | OutboxMessage | logical-domain-accounts |
| `D_OUTBOX_OutboxRetry` | D-OUTBOX | OutboxRetry | logical-domain-accounts |
| `D_PITCH` | D-PITCH | Pitch | logical-code-slots, logical-domain-booking |
| `D_PITCH_PitchStatus` | D-PITCH | PitchStatus | logical-domain-booking |
| `D_PITCH_Sport` | D-PITCH | Sport | logical-domain-booking |
| `D_POLICIES` | D-POLICIES | EmailAddressPolicy, AccountNamePolicy, PhoneNumberPolicy, PasswordPolicy, TermsAcceptancePolicy | logical-domain-accounts |
| `D_POLICIES_Profiles` | D-POLICIES | Profiles | logical-domain-accounts |
| `D_PRICEBAND` | D-PRICEBAND | PitchPriceBand | logical-code-slots, logical-domain-booking |
| `D_REFRESH` | D-REFRESH | RefreshToken | logical-domain-accounts |
| `D_SLOT` | D-SLOT | TimeSlot | logical-code-slots |
| `D_SRC` | — | backlog.md, stack.md | development-repo |
| `D_STATUSCHG` | D-STATUSCHG | OwnerStatusChange | logical-domain-accounts |
| `D_STATUSCHG_OwnerStatusAction` | D-STATUSCHG | OwnerStatusAction | logical-domain-accounts |
| `D_USER` | D-USER | AppUser | logical-domain-accounts, logical-domain-booking |
| `FEATURES` | — | Accounts, Catalogue, Booking, My bookings, Owner's clubs, Pitches, Calendar, Statistics, Admin | development-frontend |
| `F_CFG` | — | package.json, package-lock.json, next.config.ts, vitest.config.mts, eslint.config.mjs, tsconfig.json, components.json, Dockerfile | development-repo |
| `F_SRC` | — | src/ | development-repo |
| `LIBMISC` | — | lib/auth/ · redirects.ts, request-path.ts lib/guid.ts, lib/utils.ts | development-frontend |
| `L_API` | L-API | SportsBooking.Api | development-backend, logical-layers |
| `L_APP` | L-APP | SportsBooking.Application; Application | development-backend, logical-domain-accounts, logical-layers |
| `L_DOM` | L-DOM | SportsBooking.Domain; Domain | development-backend, logical-domain-accounts, logical-domain-booking, logical-layers |
| `L_INF` | L-INF | SportsBooking.Infrastructure; Infrastructure | development-backend, logical-domain-accounts, logical-domain-booking, logical-layers |
| `PIPE` | L-API | Pipeline and startup | development-di |
| `PL_BREVO` | PL-BREVO | Brevo | 01-context, 02-containers-prod, 04-deployment-prod |
| `PL_BREVO_NODE` | PL-BREVO | Brevo | physical |
| `PL_DEPLOY` | PL-DEPLOY | deploy.yml | physical, 04-deployment-prod |
| `PL_MIGBUNDLE` | PL-MIGBUNDLE | Migration bundle | physical, 04-deployment-prod |
| `PL_PRODDATA` | PL-PRODDATA | prod-data.yml | physical, 04-deployment-prod |
| `PL_RENDER` | PL-RENDER | Render (free plan, Frankfurt); Render | physical, 01-context, 02-containers-prod, 04-deployment-prod |
| `PL_RENDER_API` | PL-RENDER-API | Public web service (Docker); Public web service | physical, 02-containers-prod, 04-deployment-prod |
| `PL_RENDER_EDGE` | PL-RENDER-EDGE | Render edge | physical, 04-deployment-prod |
| `PL_SUPABASE` | PL-SUPABASE | Supabase (Free); Supabase | physical, 02-containers-prod, 04-deployment-prod |
| `PL_SUPAPOOL` | PL-SUPAPOOL | Connection pooler | physical, 04-deployment-prod |
| `PL_VERCEL` | PL-VERCEL | Vercel (Hobby); Vercel | physical, 01-context, 02-containers-prod, 04-deployment-prod |
| `PL_VERCEL_EDGE` | PL-VERCEL-EDGE | Vercel edge | physical, 04-deployment-prod |
| `PROG` | L-API | Program.cs | development-di |
| `P_ADMIN` | P-ADMIN | Admin | scenarios-use-cases, 01-context |
| `P_CLI` | P-CLI | Customer without an account | 01-context |
| `P_DONO` | P-DONO | Owner | scenarios-use-cases, 01-context |
| `P_JOG` | P-JOG | Player | scenarios-use-cases, 01-context |
| `P_OPS` | P-OPS | Operator | process-runtime, 01-context |
| `P_VIS` | P-VIS | Visitor | scenarios-use-cases, 01-context |
| `ROOT` | — | / · repository root | development-repo |
| `SC_01` | SC-01 | Searches for and books a slot | scenarios-use-cases |
| `SC_02` | SC-02 | Sets up club, pitches, schedules and prices | scenarios-use-cases |
| `SC_03` | SC-03 | Cancels a booking | scenarios-use-cases |
| `SC_04` | SC-04 | Weekly booking | scenarios-use-cases |
| `SC_05` | SC-05 | Recovers the password | scenarios-use-cases |
| `SC_06` | SC-06 | Deactivates an owner | scenarios-use-cases |
| `SC_07` | SC-07 | Race for the same slot | scenarios-use-cases |
| `SYS` | — | Pitch Booking App | 01-context |
| `S_SRC` | — | check-architecture-docs.sh, check-architecture-docs.ps1, generate-icons.mjs | development-repo |
| `WEB_API_CLIENT` | WEB-API-CLIENT | API client (browser); tab A; API client | development-frontend, process-refresh-client, process-refresh, 03-components-web |
| `WEB_API_CLIENT_B` | WEB-API-CLIENT | tab B | process-refresh-client |
| `WEB_AREA_ACCOUNT` | WEB-AREA-ACCOUNT | Account pages | development-frontend, 03-components-web-features, 03-components-web |
| `WEB_AREA_ADMIN` | WEB-AREA-ADMIN | Admin area | development-frontend, 03-components-web-features, 03-components-web |
| `WEB_AREA_MANAGE` | WEB-AREA-MANAGE | Owner area; app/manage/layout.tsx | development-frontend, process-guard, 03-components-web-features, 03-components-web |
| `WEB_AREA_PLAYER` | WEB-AREA-PLAYER | Player area | development-frontend, 03-components-web-features, 03-components-web |
| `WEB_DATETIME` | WEB-DATETIME | Dates in Lisbon time | development-frontend |
| `WEB_F_ADMIN` | WEB-F-ADMIN | Owner page; Admin | process-owner-deactivation, 03-components-web-features |
| `WEB_F_AUTH` | WEB-F-AUTH | Accounts; Login page | process-login, process-refresh-client, process-reset-confirm, process-reset-request, 03-components-web-features |
| `WEB_F_BOOKING` | WEB-F-BOOKING | Booking | process-book, process-series, 03-components-web-features |
| `WEB_F_CALENDAR` | WEB-F-CALENDAR | Calendar | process-cancel-club, 03-components-web-features |
| `WEB_F_CATALOG` | WEB-F-CATALOG | Catalogue | 03-components-web-features |
| `WEB_F_CLUBS` | WEB-F-CLUBS | Owner's clubs | 03-components-web-features |
| `WEB_F_MYBOOK` | WEB-F-MYBOOK | My bookings | process-cancel-player, 03-components-web-features |
| `WEB_F_PITCHES` | WEB-F-PITCHES | Pitches | 03-components-web-features |
| `WEB_F_STATS` | WEB-F-STATS | Statistics | 03-components-web-features |
| `WEB_GUARDS` | WEB-GUARDS | Area guards; AreaGuard; Guards | development-frontend, process-guard, 03-components-web |
| `WEB_PROXY` | WEB-PROXY | src/proxy.ts; proxy.ts | development-frontend, process-guard, 03-components-web |
| `WEB_REWRITE` | WEB-REWRITE | Rewrites | 03-components-web |
| `WEB_SERVER` | WEB-SERVER | Server-side API calls; lib/server/session.ts; Server-side calls | development-frontend, process-guard, 03-components-web |
| `WEB_UI` | WEB-UI | Shared components | development-frontend |
| `W_SRC` | — | ci.yml | development-repo |
| `X_GHA` | X-GHA | GitHub Actions | physical, 04-deployment-prod |
| `X_MAILPIT` | X-MAILPIT | Mailpit; mailpit service | 02-containers-dev, 04-deployment-dev |
| `X_SMTP` | X-SMTP | SMTP server | process-outbox, process-reset-request, process-runtime, 01-context, 03-components-api-notifications |
