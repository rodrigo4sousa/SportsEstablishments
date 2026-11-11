# C4: context, containers and deployment

<!-- C4 model · levels 1 (context) and 2 (containers) and deployment diagrams. See README.md. -->

The most general C4 diagrams of the system: the context (level 1), the containers (level 2) and the deployment, in development and in production. The components (level 3) and the code (level 4) are in the [logical view](../4plus1/logical.md), and the sequence diagrams in the [process view](../4plus1/process.md). The ids are in the [inventory](../inventory.md); the `.puml` and SVG files of all the diagrams are alongside, in `c4/` and `4plus1/` (sources) and in `out/` (drawings).

For each level, who it is for and which decision it documents:

- **Context** (level 1). **For:** evaluators, product, newcomers. **Documented decision:** a single marketplace, with a player account that works at every club of every owner and no per-owner sites ([ADR 0005](../adr/0005-single-marketplace.md)), and three areas (player, owner, admin) on top of the same API ([ADR 0001](../adr/0001-one-app-three-areas.md)).
- **Containers** (level 2). **For:** newcomers and operators. **Documented decision:** the browser sees a single origin, which keeps the session cookies first-party and makes CORS unnecessary ([ADR 0002](../adr/0002-session-in-httponly-cookies.md)); the email worker runs in the API process and reads the outbox ([ADR 0004](../adr/0004-outbox-for-emails.md)).
- **Deployment.** **For:** whoever deploys and operates. **Documented decision:** production on Vercel, Render and Supabase, with Brevo for email ([ADR 0009](../adr/0009-deployment-target.md)); development is docker compose.

## Level 1 · System context

<!-- C4 level 1 · 4+1 view: scenarios (context of use). See README.md. -->

### Context diagram

![System context](../out/01-context.svg)

Source: [01-context.puml](01-context.puml). All the elements exist; the production ones (ids `PL-`) have been running since 2026-11-09 (T06).

### Scope

The Pitch Booking App is a **single marketplace**: one system where players book pitches (futsal, 7-a-side football, padel, tennis) at Portuguese clubs. A player has one account that works at every club of every owner; there are no per-owner sites or domains. The same system has three areas (player, owner, admin) on top of the same API. There are no payments: the booking stores the price at the time.

Out of scope: payments, mobile apps, integration with other booking systems, bookings after midnight (known MVP limitation) and any way for the admin to see players' personal data.

### People

| Id | Role | What they do with the system |
| --- | --- | --- |
| P-VIS | Visitor | Searches clubs and sees free times; to book, goes to login. |
| P-JOG | Player | Books (a slot or a weekly series), sees and cancels their bookings, receives emails. |
| P-DONO | Owner | Manages clubs, pitches, schedules, blocks, prices and the calendar; books for customers; marks no-shows. |
| P-ADMIN | Admin | Sees counts and charts and deactivates or reactivates owners. Exists only by seed. |
| P-CLI | Customer without an account | Does not use the system: asks the club, and the owner books on their behalf. Receives no email. |
| P-OPS | Operator | Starts the application, sets the configuration and runs `seed-demo` and `set-admin-password`. |

### External systems

| Id | System | Connection |
| --- | --- | --- |
| X-SMTP | SMTP server | The application sends booking and account emails over SMTP. In development it is X-MAILPIT (nothing goes out); in production it is PL-BREVO. |
| PL-BREVO | Brevo | The production SMTP server. |
| PL-VERCEL | Vercel | The platform where the site runs. It is just a box: what it contains is in the [containers](#container-diagram). |
| PL-RENDER | Render | The platform where the API runs. It is just a box: what it contains is in the [containers](#container-diagram). |

CI (X-GHA) and the repository (X-GH) are not part of using the system and do not appear here; they are in the deployment view.

### Context notes

- Vercel and Render (T06) appear as external systems to mark that hosting is a deployment choice and not the system's. No business rule depends on them.
- Sending email never blocks a booking: the application saves the email in an outbox and sends it later (see [4plus1/process.md](../4plus1/process.md)).

## Level 2 · Containers

<!-- C4 level 2 · 4+1 view: process. See README.md. -->

Two diagrams: what runs today in development and what runs in production. In both the browser sees **a single origin**, which keeps the session cookies first-party and makes CORS unnecessary.

### Container diagram

#### Development (docker compose)

![Containers in development](../out/02-containers-dev.svg)

Source: [02-containers-dev.puml](02-containers-dev.puml).

#### Production (T06)

![Containers in production (T06)](../out/02-containers-prod.svg)

Source: [02-containers-prod.puml](02-containers-prod.puml). **In production since 2026-11-09 (T06).**

### Containers

| Id | Technology | Responsibility | Owner of |
| --- | --- | --- | --- |
| C-BROWSER | Browser | Runs the Client Components and keeps the httpOnly cookies `access_token` and `refresh_token`. Talks only to C-WEB (in development, also to the API on 5080, for Swagger). | The session cookies. |
| C-WEB | Next.js 16 (App Router, `standalone` image), port 3000 | Serves the player, owner and admin areas. Server Components call the API at the internal URL, passing on the user's cookie; Client Components call `/api/*` on the same origin. Forwards `/api/*` to the API (WEB-REWRITE). | No data: it does not access the database. |
| C-API | .NET 10, ASP.NET Core, port 8080 (5080 on the host, development only) | Sole owner of the business rules, authentication (JWT and refresh in cookies), authorization by profile and by owner, ProblemDetails errors and rate limiting. Checks the `Origin` of requests that change data. | All the rules and all access to C-DB. |
| C-WORKER | `BackgroundService` in the API process | Reads the outbox, composes and sends the emails with retries; uses `SKIP LOCKED`, so it can run in several instances. | Sending the emails (the outbox itself is in C-DB). |
| C-DB | PostgreSQL 18.6 (development); managed PostgreSQL (production, T06) | Stores the data, enforces the rules that cannot fail (no overlapping confirmed bookings, append-only owner history) and stores the Data Protection keys. | The data and the schema. |

C-CLI (`seed-demo` and `set-admin-password`) is the same executable as the API without starting the server, so it has no box of its own in these diagrams.

### Communication between containers

**Development**

- C-BROWSER → C-WEB: HTTP on port 3000, with cookies. This is the normal entry point.
- C-WEB → C-API: Next.js forwards `/api/*` to `http://api:8080`, and Server Components read the API at the same address (`API_INTERNAL_URL`). The address is a build argument (the rewrites are fixed at build time) and an environment variable at run time.
- C-BROWSER → C-API: the API is also published on port 5080 (Swagger at `/swagger`), and `http://localhost:5080` is one of the origins accepted in Development. It is a direct entry point for tools, outside the normal path.
- C-API and C-WORKER → C-DB: SQL with Npgsql, `Host=postgres;Port=5432`.
- C-WORKER → X-MAILPIT: SMTP on port 1025, without TLS; the web interface is on 8025.

**Production (T06)**

- C-BROWSER → C-WEB: HTTPS, at Vercel's origin (PL-VERCEL). It is the only origin the browser knows.
- C-WEB → C-API: HTTPS over the Internet, to the public URL of the API service on Render (PL-RENDER-API, `API_INTERNAL_URL`), both in the `/api/*` rewrite and in Server Components. The API can also be reached directly: the `Origin` and the limits protect it, and the per-IP limits are best-effort only.
- C-API and C-WORKER → C-DB (PL-SUPABASE): SQL through the pooler in session mode, as `sportsbooking_app`.
- C-WORKER → PL-BREVO: SMTP on port 2525 with StartTls (confirmed on the first deploy), because Render's free plan blocks ports 25, 465 and 587.
- Cookies: they are httpOnly and Secure; `refresh_token` has `Path=/api/auth`. Since the browser only knows Vercel's origin, they are first-party. The API checks that the `Origin` is the site's (`Security__AllowedOrigins__0`).

### Container notes

- **The reverse proxy is Next.js, not Caddy.** In development and in compose it is C-WEB that forwards `/api/*`; in production it is the Next.js rewrite, served by Vercel. The old design (Caddy on a virtual machine, in `docs/stack.md`) was not adopted (see [ADR 0009](../adr/0009-deployment-target.md)).
- **The client IP.** With no proxy configured, the API ignores `X-Forwarded-For` and uses the connection's address; that is how it is in compose, because the rewrite passes on an `X-Forwarded-For` sent by the browser. In production the configuration is different and provisional (see [section 4.4 of the physical view](../4plus1/physical.md#44-the-client-ip)).
- The worker runs in the API process: in production (T06) emails and reminders only go out while the API is awake.
- Migrations run at API startup in development only. In production GitHub Actions applies them, with a migration bundle, as the schema owner (see [section 4.8 of the physical view](../4plus1/physical.md#48-where-migrations-and-commands-run)).

## Deployment

<!-- C4 deployment diagrams · 4+1 view: physical. See README.md. -->

Where each container runs, in development (which exists) and in production (running since 2026-11-09, T06). The sections are those of the [physical view](../4plus1/physical.md), which has the rest: 2 (continuous integration), 4 (network and trust), 5 (risks) and 6 (notes).

### 1. Development (docker compose)

![Deployment in development](../out/04-deployment-dev.svg)

Source: [04-deployment-dev.puml](04-deployment-dev.puml).

One machine (DEP-DEVHOST) with Docker. `docker compose up --build` creates the `sportsbooking` project with compose's default network (DEP-COMPOSE-NET), where the services call each other by name (`api`, `postgres`, `mailpit`).

| Service | Element | Image or build | Ports (host → container) | Volumes | Starts after | Environment (from `docker-compose.yml`) |
| --- | --- | --- | --- | --- | --- | --- |
| `postgres` | C-DB | `postgres:18.6` | `5432 → 5432` | `postgres-data` (DEP-PGVOLUME) at `/var/lib/postgresql` | — | `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` (from `.env`, default `sportsbooking`). `pg_isready` health check every 5 s. |
| `mailpit` | X-MAILPIT | `axllent/mailpit:v1.31.6` | `8025 → 8025` (web), `1025 → 1025` (SMTP) | — | — | — |
| `api` | C-API, C-WORKER | build of `./backend` (`backend/Dockerfile`: SDK 10.0 → `aspnet:10.0`, user `$APP_UID`, `EXPOSE 8080`) | `5080 → 8080` | — | `postgres` healthy, `mailpit` started | `ASPNETCORE_ENVIRONMENT=Development`; `ConnectionStrings__Default` (`Host=postgres;Port=5432;…;GSS Encryption Mode=Disable`); `AdminSeed__Email`/`AdminSeed__Password` (from `ADMIN_SEED_*` in `.env`, empty by default); `Email__Host=mailpit`, `Email__Port=1025`, `Email__Security=None`. |
| `frontend` | C-WEB | build of `./frontend` (`frontend/Dockerfile`: `node:22-alpine`, `output: "standalone"`, user `node`) with the build argument `API_INTERNAL_URL` | `3000 → 3000` | — | `api` (started, not healthy) | `API_INTERNAL_URL=http://api:8080`, defined once (`x-api-internal-url`) and used as a build argument and as an environment variable. |

What comes from `appsettings.Development.json` (because the environment is Development) and not from compose: `Jwt:SigningKey` (a development-only key), `Email:FromAddress`, `PublicSite:BaseUrl=http://localhost:3000` and `Security:AllowedOrigins` (`http://localhost:3000` and `http://localhost:5080`).

**Consequences of the Development environment:**

- The API applies the migrations at startup (`Program.cs`), runs the admin seed and shows Swagger at `/swagger` (Development only).
- The browser talks to `http://localhost:3000`; the cookies are `Secure` but browsers accept them on `localhost` without HTTPS (comment in `Api/Auth/AuthCookies.cs`).
- The API has a second entry point, `localhost:5080`, for tools ([Architecture notes](../README.md#known-limitations), 1). PostgreSQL is also published on `localhost:5432`, to run the API outside Docker (`dotnet run`, with the connection from `appsettings.Development.json`) and for SQL clients.
- The data stays in the `postgres-data` volume: it survives `docker compose down` and is only deleted with `docker compose down -v`.
- The command-line commands (C-CLI) run as `docker compose run --rm api seed-demo` (or `set-admin-password`, with `ADMIN_NEW_PASSWORD`): a short-lived container on the same network.

### 3. Production (T06)

![Deployment in production (T06)](../out/04-deployment-prod.svg)

Source: [04-deployment-prod.puml](04-deployment-prod.puml). **In production since 2026-11-09 (T06).** The manual steps are in the [runbook](../../deployment/runbook.md).

| Node or service | Element | Runs | Public? | Note |
| --- | --- | --- | --- | --- |
| Vercel (Hobby) | PL-VERCEL, PL-VERCEL-EDGE | C-WEB | Yes, at `https://<project>.vercel.app` | **The origin the browser knows.** The `/api/*` rewrite is fixed at build time. |
| Render (free) | PL-RENDER, PL-RENDER-EDGE, PL-RENDER-API | C-API with C-WORKER, image from `backend/Dockerfile` | **Yes**, at `https://<service>.onrender.com` | Goes to sleep after 15 minutes without traffic. Automatic deploy turned off. |
| Supabase (free) | PL-SUPABASE, PL-SUPAPOOL, C-DB | Managed PostgreSQL and the pooler | Yes (Internet endpoint, with password and TLS) | Always through the pooler in session mode (section 4.6). |
| Brevo | PL-BREVO | SMTP | — (provider) | Port 2525 with StartTls (confirmed on the first deploy); sender verified by email. |
| GitHub Actions | X-GHA, PL-DEPLOY, PL-PRODDATA, PL-MIGBUNDLE | `deploy.yml`, `prod-data.yml` | — | Section 4.8. |

## Elements of each diagram

The diagram boxes show only the name and, in square brackets, the technology. Each element's responsibility, as it used to be written inside the box, is in these tables; the correspondence between names and ids is in [section 12 of the inventory](../inventory.md#12-names-and-aliases-in-the-diagrams).

### System context

Source: [01-context.puml](01-context.puml).

| Name | Technology | Responsibility |
| --- | --- | --- |
| Visitor | — | Searches clubs and sees free times, without a session. |
| Player | — | Books, cancels and sees their bookings. |
| Owner | — | Manages clubs, pitches, calendar and bookings. |
| Admin | — | Platform owner: counts and owners' status. |
| Customer without an account | — | Asks the club for a booking; does not use the system. |
| Operator | — | Starts the application and sets the configuration. |
| Pitch Booking App | — | Single marketplace for booking pitches (futsal, 7-a-side football, padel, tennis) at Portuguese clubs. |
| SMTP server | — | Receives the application's emails. In development it is X-MAILPIT. |
| Brevo | — | The production SMTP server. |
| Vercel | — | Where the site runs. |
| Render | — | Where the API runs. |

### Containers: development

Source: [02-containers-dev.puml](02-containers-dev.puml).

| Name | Technology | Responsibility |
| --- | --- | --- |
| User's browser | Browser | Runs the Client Components; keeps the httpOnly session cookies. |
| Next.js application | Next.js 16, Node.js, port 3000 | Serves the pages and forwards /api/\* to the API (WEB-REWRITE); it is today's reverse proxy. |
| ASP.NET Core API | .NET 10, controllers | Business rules, cookie authentication, database access. |
| Email worker | BackgroundService | Reads the outbox and sends the emails with retries. |
| Database | PostgreSQL 18.6, port 5432 | All the data, the rules that cannot fail and the Data Protection keys. |
| Mailpit | — | SMTP 1025, web interface 8025 |

### Containers: production

Source: [02-containers-prod.puml](02-containers-prod.puml).

| Name | Technology | Responsibility |
| --- | --- | --- |
| User's browser | Browser | Talks only to Vercel's origin; keeps the httpOnly session cookies. |
| Next.js application | Next.js 16 | Pages over HTTPS; the /api/\* rewrite sends to the API. |
| ASP.NET Core API | .NET 10, controllers | Business rules, cookies, database. |
| Email worker | BackgroundService | Sends the outbox emails while the API is awake. |
| Database | Managed PostgreSQL | All the data and the Data Protection keys. |
| Brevo | — | Production SMTP server |

### API components: overview

Source: [03-components-api-overview.puml](03-components-api-overview.puml).

| Name | Technology | Responsibility |
| --- | --- | --- |
| Next.js application | Next.js 16 | Forwards the browser's /api/\* and reads the API in Server Components. |
| Accounts and sessions | AuthController, App/Accounts, App/Auth, Inf/Identity, Inf/Auth | Registration, login, logout, me, refresh, password recovery. |
| Public catalogue | ClubsController, LocalitiesController, App/Catalog | Search, localities, club page, free slots. Read-only, anonymous. |
| Player bookings | BookingsController, MeBookingsController | A single booking, weekly series, my bookings, cancelling. |
| Owner's clubs | ManageClubsController, ClubService | Create, list and edit clubs. |
| Pitches | ManagePitchesController, PitchService, OwnedPitches | The club's pitches; OwnedPitches is the way into the owner's pitches. |
| Schedules | ManagePitchScheduleController, PitchScheduleService | Weekly schedule and slot preview. |
| Blocks | ManageBlocksController, BlockService | Block and remove periods. |
| Price bands | ManagePriceBandsController, PriceBandService | Price bands by day and time. |
| Owner's calendar and bookings | ManageCalendarController, ManageBookingsController | Calendar, details, cancelling, booking for a customer, no-shows. |
| Owner's statistics | ManageStatisticsController, OccupationService | Occupation, hours, revenue. |
| Administration | AdminController, App/Admin, Inf/Admin | Totals, charts, owners, top 10, deactivate and reactivate. |
| Notifications (outbox) | App/Notifications, Inf/Notifications | Puts emails in the outbox; composes them when they are sent. |
| A day's slots and «free» | PitchDayCalendar, DaySlot | The single calculation of a day's slots with blocks and bookings. |
| Persistence | IAppDbContext, AppDbContext | Application's view of the database; migrations. |
| PostgreSQL | PostgreSQL 18.6 | Data, exclusion constraint, triggers. |

### API components: bookings

Source: [03-components-api-bookings.puml](03-components-api-bookings.puml).

| Name | Technology | Responsibility |
| --- | --- | --- |
| Single booking | BookingService.CreateAsync | Validates, finds the pitch, confirms the owner is active, locks the pitch, checks the slot, price from PriceForSlot, inserts. |
| Weekly series | RecurringBookingService, RecurringOccurrences | Preview and creation: each date in BookOneAsync, with its own lock and its own transaction. |
| My bookings and cancelling | MyBookingsService, BookingCancellationService | List and detail filtered by the session's player; cancelling with Booking.CancelByPlayer. |
| Booking store | IBookingStore, Inf/Bookings/BookingStore | TryAddAsync and TrySaveChangesAsync: 23P01 → false (409 slot_taken). |
| Owner bookings | ClubBookingService, ClubCalendarService | Booking for a customer, cancelling with a reason, marking and undoing a no-show, calendar. |
| Per-owner authorization | OwnedClubs, OwnedPitches, OwnedBookings | Only the session owner's clubs, pitches and bookings (the same 404 for another owner's). |
| Slot check | BookingSlotCheck.CheckAsync | Active pitch → window → grid (SlotStartingAt) → started → blocks. Does not look at other bookings. |
| Pitch lock | IPitchLock, Inf/Pitches/PitchLock | Transaction + SELECT … FOR UPDATE on the pitch's row. |
| Visible clubs | VisibleClubs | Active owner and at least one active pitch. |
| Accounts | IAccountStore.IsActiveAsync | The club's owner is not deactivated. |
| Outbox | BookingEmailOutbox | Attaches the messages to the same IAppDbContext, before the caller's SaveChanges. |
| pitches | Table | The row locked by CC-LOCK. |
| blocks | Table | The pitch's blocks. |
| bookings | Table | Bookings; never deleted. |
| bookings_no_overlap | Exclusion constraint (GiST) | No Confirmed bookings overlap on the same pitch. |
| outbox_messages | Table | Emails waiting to be sent. |

### API components: accounts

Source: [03-components-api-accounts.puml](03-components-api-accounts.puml).

| Name | Technology | Responsibility |
| --- | --- | --- |
| Registration | AccountRegistrationService | Player and owner; acceptance of the Terms (400 terms_not_accepted); 409 email_taken; starts the session. |
| Login | LoginService | Any failure is the same 401 invalid_credentials. |
| Recovery | PasswordResetService, Inf/Auth/PasswordResetStore, AccountEmailBuilder | Request (always 202) and confirmation; the token is only made by the worker. |
| Accounts in Identity | IAccountStore, Inf/Identity/AccountStore, UserManager&lt;AppUser> | Creates, authenticates (FallbackPasswordHash, LoginLockout), looks up, IsActiveAsync. |
| Sessions and cookies | ISessionService, Inf/Auth/SessionService, Api/Auth/AuthCookies | 15 min JWT (sub, role) and 7-day refresh in httpOnly cookies; atomic rotation. |
| Data Protection | Identity DataProtectorTokenProvider | Makes and checks the recovery tokens; reads the system clock. |
| Admin seed | AdminAccountSeeder | At startup; the only code that gives the Admin profile. |
| set-admin-password | AdminPasswordCommand | Changes the hash and revokes the admin's sessions. |
| Owner status | AdminOwnerStatusService, Inf/Admin/OwnerStatusStore | Deactivates and reactivates owners in a transaction. |
| Outbox | OutboxMessage | password_reset, password_changed. |
| users, roles, user_roles | Tables (Identity) | deactivated_at, registered_at, accepted_terms_version, terms_accepted_at, unique normalized email. |
| refresh_tokens | Table | Only the value's SHA-256. |
| data_protection_keys | Table | The tokens' keys. |
| owner_status_changes | Table, append-only | Owner history. |
| outbox_messages | Table | Emails waiting to be sent. |

### API components: notifications

Source: [03-components-api-notifications.puml](03-components-api-notifications.puml).

| Name | Technology | Responsibility |
| --- | --- | --- |
| Player bookings | BookingService, RecurringBookingService, BookingCancellationService | Booking, series, cancelling. |
| Owner bookings | ClubBookingService.CancelAsync | Cancelling by the club (a booking for a customer generates no email). |
| Recovery | PasswordResetStore, AccountEmailBuilder | Queues; at send time, makes the token and the link. |
| Put in the outbox | BookingEmailOutbox, OutboxMessage.Create | Attaches the message to the caller's IAppDbContext; does not save. |
| Worker loop | OutboxDispatcher | One scope per pass; waits the interval on TimeProvider; Outbox:Enabled. |
| Process what is due | OutboxProcessor.ProcessDueAsync | SKIP LOCKED; sends, skips, gives up or tries again (OutboxRetry). |
| Compose the emails | BookingEmailComposer, BookingSeriesEmailComposer, BookingNotificationComposer, AccountEmailComposer | The recipient's language (pt-PT or English), Lisbon time, text and HTML. |
| Send | IEmailSender, SmtpEmailSender (MailKit) | One connection per message. |
| Data Protection | Identity | Recovery token (system clock). |
| Injected clock | TimeProvider | The current time and the wait between passes. |
| outbox_messages | Table | ck_outbox_messages_one_outcome: at most one outcome. |
| ux_outbox_messages_booking_notification | Partial unique index | One reminder and one owner notice per booking. |
| users | Table | The recipient's current address and name. |
| bookings | Table | Current state of the booking for the reminder and the notice. |
| SMTP server | — | Mailpit in development |

### Front end components: routes and guards

Source: [03-components-web.puml](03-components-web.puml).

| Name | Technology | Responsibility |
| --- | --- | --- |
| Browser | Browser | Runs the Client Components; keeps the httpOnly cookies. |
| proxy.ts | Next.js proxy | On /manage/\*, /admin/\* and /bookings writes the requested path to x-pathname; answers 404 to impossible ids and to public clubs the API does not have. Does not renew sessions. |
| Player area | app/(player) | / (search in the address), /clubs/[slug] (choice ?pitch=&date=&slot=&weeks= in the address), /bookings. |
| Owner area | app/manage | Clubs, pitches, calendar (?date=&view=&pitch=&booking= in the address), statistics. |
| Admin area | app/admin | Dashboard and an owner's page; filters in the address. |
| Account pages | app/login, register, forgot-password, reset-password | Account forms. |
| Guards | AreaGuard, PlayerGuard, GuestOnly, lib/auth/redirects | Redirect by profile on the server; the API always decides. |
| Server-side calls | lib/server (server-only) | getSessionAccount (user's cookie), loadPublicClub (no cookies), loadAdminStats; no-store. |
| API client | lib/api/client.ts (runs in the browser) | apiRequest/apiJson: ProblemDetails → ApiError; one shared refresh on 401 (Web Lock). |
| Rewrites | next.config.ts | /api/\* → API_INTERNAL_URL/api/\*; headers for /reset-password. |
| ASP.NET Core API | .NET 10 | Rules, sessions and data. |

### Front end components: features

Source: [03-components-web-features.puml](03-components-web-features.puml).

| Name | Technology | Responsibility |
| --- | --- | --- |
| Player area | app/(player) | /, /clubs/[slug], /bookings |
| Account pages | app/login, register, … | Login, registration, recovery. |
| Owner area | app/manage | /manage/... |
| Admin area | app/admin | /admin, /admin/owners/[id] |
| Accounts | features/auth | Forms; useCurrentUser (key [auth, me]). |
| Catalogue | features/catalog | Search in the address; public slots. |
| Booking | features/booking | Choice in the address (selection.ts); weekly series. |
| My bookings | features/my-bookings | Tab and page in the address; cancelling. |
| Owner's clubs | features/clubs | The club's list and form. |
| Pitches | features/pitches | Pitches, schedule, preview, blocks, price bands. |
| Calendar | features/calendar | View in the address (calendar-url.ts); real-time layout. |
| Statistics | features/statistics | Period and pitch in the address. |
| Admin | features/admin | View in the address (admin-url.ts); charts. |

### Deployment: development

Source: [04-deployment-dev.puml](04-deployment-dev.puml).

| Name | Technology | Responsibility |
| --- | --- | --- |
| Browser | Browser | Opens http://localhost:3000; keeps the httpOnly cookies. |
| frontend service | Next.js 16 standalone, Node 22, port 3000 → localhost:3000 | API_INTERNAL_URL=http://api:8080, as a build argument and environment variable. |
| api service | .NET 10, port 8080 → localhost:5080 | ASPNETCORE_ENVIRONMENT=Development: migrations at startup, Swagger at /swagger. |
| Email worker | BackgroundService in the API process | Sends the outbox to Mailpit. |
| postgres service | postgres:18.6, port 5432 → localhost:5432 | pg_isready health check; api only starts with it healthy. |
| mailpit service | axllent/mailpit:v1.31.6, SMTP 1025 and web 8025 → localhost | Catches all the emails; none goes out. |
| postgres-data volume | Docker volume | Mounted at /var/lib/postgresql; survives docker compose down. |

### Deployment: production

Source: [04-deployment-prod.puml](04-deployment-prod.puml).

| Name | Technology | Responsibility |
| --- | --- | --- |
| User's browser | Browser | Talks only to Vercel's origin; first-party cookies. |
| Vercel edge | Managed by Vercel | HTTPS on \*.vercel.app; /api/\* rewrite fixed at build time; writes X-Forwarded-For with the visitor's IP. |
| Next.js application | Next.js 16, Vercel Functions | Pages and Server Components, which pass on the cookie and the visitor's IP. |
| Render edge | Cloudflare and load balancers | TLS on \*.onrender.com; the X-Forwarded-For chain is not documented. |
| ASP.NET Core API | .NET 10, PORT on 0.0.0.0 | Production; no migrations at startup; /health is liveness only. |
| Email worker | BackgroundService in the API process | Only runs while the API is awake. |
| Connection pooler | Supavisor, session mode (port 5432, IPv4) | Never transaction mode or the direct host (IPv6). |
| Database | Managed PostgreSQL | Data, btree_gist, Data Protection keys; two roles (PL-DBUSERS). |
| Brevo | — | Production SMTP, port 2525 (confirmed on the first deploy); sender verified by email. |
| deploy.yml | After green CI on main | Migrations, API, site, smoke, in this order. |
| prod-data.yml | Manual | migrate-only, set-admin-password, seed-demo. |
| Migration bundle | dotnet ef migrations bundle | Runs as the schema owner. |
