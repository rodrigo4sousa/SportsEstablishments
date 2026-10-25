# Sports pitch booking

Web app for booking sports pitches and courts (futsal, futebol 7, padel, ténis). Players search clubs and book free slots; club owners manage pitches, opening hours and bookings; the platform admin sees aggregate numbers.

- **Frontend:** Next.js 16 (App Router), TypeScript strict, Tailwind CSS v4, shadcn/ui
- **Backend:** ASP.NET Core 10 Web API, Clean Architecture, EF Core 10 + Npgsql
- **Database:** PostgreSQL 18
- **Dev email:** Mailpit (no real email leaves your machine)

Product backlog: [docs/backlog.md](docs/backlog.md). Architecture and practices: [docs/stack.md](docs/stack.md). Both documents are in Portuguese.

## Run everything with Docker

Prerequisite: Docker Desktop (or Docker Engine with Compose v2).

```bash
cp .env.example .env        # optional; the defaults are local-only values
docker compose up --build
```

| Service | URL |
| --- | --- |
| App | http://localhost:3000 |
| API | http://localhost:5080 (health: `/health`) |
| Swagger UI | http://localhost:5080/swagger |
| Mailpit (test inbox) | http://localhost:8025 |
| PostgreSQL | `localhost:5432` (user/password/db `sportsbooking`) |

The browser only talks to the app. Next.js rewrites `/api/*` to the API, so frontend and API share one origin and the auth cookies stay first-party. EF Core migrations are applied automatically when the API starts in Development; in other environments they must be applied before the API starts (see [Admin account](#admin-account)).

Stop with `docker compose down` (add `-v` to also delete the database volume).

## Run locally without Docker for the app

Prerequisites: [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) and Node.js 22+. PostgreSQL can still run from compose:

```bash
docker compose up -d postgres mailpit

# API on http://localhost:5080 (Development profile, connection string in appsettings.Development.json)
dotnet run --project backend/src/SportsBooking.Api

# Frontend on http://localhost:3000, with /api/* rewritten to http://localhost:5080
cd frontend
npm install
npm run dev
```

Set `API_INTERNAL_URL` to point the frontend at a different API address. `docker-compose.yml` defines it once (`http://api:8080`) and passes it to the image build (the rewrites are fixed at build time) and to the frontend container's environment (server code reads it at run time).

## Sessions and security settings

Sign-in uses two httpOnly cookies (a 15-minute JWT and a rotating refresh token, see [docs/stack.md](docs/stack.md)). They are `Secure`: Chrome, Edge and Firefox treat `http://localhost` as secure and keep them; if your browser drops them over plain HTTP, use one of those for local development.

| Setting | Development | Other environments |
| --- | --- | --- |
| `Jwt:SigningKey` (32+ bytes, secret) | local-only value in `appsettings.Development.json` | env `Jwt__SigningKey`, required |
| `Security:AllowedOrigins` (sites allowed to send POST/PUT/PATCH/DELETE) | `http://localhost:3000` (app) and `http://localhost:5080` (Swagger) | env `Security__AllowedOrigins__0=https://<your site>`, required |

The API refuses to start if either is missing or invalid. Mutating requests without the site's `Origin` header get 403, so for `curl` add `-H "Origin: http://localhost:3000"`.

### Rate limiting and trusted proxies

Login, registration and refresh are rate limited per client IP (429 `too_many_requests`). Five wrong passwords lock an account for 15 minutes. Both sets of values are provisional (US03 in [docs/backlog.md](docs/backlog.md)).

| Setting | Default |
| --- | --- |
| `RateLimiting:Auth:Window` | `00:01:00` |
| `RateLimiting:Auth:LoginPermitLimit` / `RegisterPermitLimit` / `RefreshPermitLimit` | 10 / 5 / 30 requests per window |
| `ForwardedHeaders:KnownProxies` (IP addresses) and `ForwardedHeaders:KnownNetworks` (CIDR) | none |

The client IP is the connection's address. `X-Forwarded-For` is believed only from a configured proxy; from anyone else it is ignored, so a client cannot pick its own rate-limit partition. The API refuses to start if an entry is not a valid address or network.

- **Development (`docker compose`, `dotnet run`): configure nothing.** The browser reaches the API through the Next.js rewrite, so every request comes from the frontend container (or localhost) and shares one limit, which is fine for one developer. Don't trust the Next.js container: its rewrite passes on an `X-Forwarded-For` sent by the browser unchanged, so trusting it would let any client choose its own IP.
- **Production: trust only Caddy.** Give the Caddy container a fixed address on the network it shares with the API and set `ForwardedHeaders__KnownProxies__0=<that address>`. Caddy replaces `X-Forwarded-For` with the real client address unless it is told to trust an upstream proxy, so leave its `trusted_proxies` unset. Don't trust the whole network: any other container on it could then spoof the header. (Deploy is T06.)

## Admin account

There is no public sign-up for the platform admin. The API creates the admin account at startup from two settings, if it does not exist yet:

| Setting | Docker compose (`.env`) | Environment variable |
| --- | --- | --- |
| Email | `ADMIN_SEED_EMAIL` | `AdminSeed__Email` |
| Password (8+ characters) | `ADMIN_SEED_PASSWORD` | `AdminSeed__Password` |

For `dotnet run`, use user-secrets instead of files:

```bash
dotnet user-secrets --project backend/src/SportsBooking.Api set "AdminSeed:Email" "you@example.com"
dotnet user-secrets --project backend/src/SportsBooking.Api set "AdminSeed:Password" "<a long password>"
```

- **Both settings missing:** the API starts without an admin and logs a warning.
- **Only one set, a malformed email or a password under 8 characters:** the API refuses to start. The error names the setting, never its value. Whitespace around the email is trimmed first.
- The seed never changes an existing admin (changing the password setting later has no effect), never creates a second admin and never promotes an existing player or owner account (it logs a warning instead).

The seed runs in **every environment**, right after migrations, so the database schema must be up to date before the API starts. Development (and `docker compose`) applies migrations on startup; production applies them at deploy time with a migration bundle, before starting the API.

### Changing the admin password

There is no endpoint for it, and the seed never touches an existing admin. The API has a command for it, `set-admin-password`
(like `seed-demo`): it changes the admin's password, ends all of the admin's sessions, and exits without starting the server.

The new password is read from the environment variable `ADMIN_NEW_PASSWORD`, **never from the command line** (an argument
shows in the process list and the shell history, and the command refuses any argument). It follows the same rule as every password
(at least 8 characters). The command never prints or logs the password, nor the admin's email. Exit code 0 means it changed, 1 that
it refused (no variable, a password that is too short, no admin account) and changed nothing.

```bash
# with docker compose (the container gets the variable from your shell; type it without echo, so it stays out of the history)
read -rs ADMIN_NEW_PASSWORD && export ADMIN_NEW_PASSWORD
docker compose run --rm -e ADMIN_NEW_PASSWORD api set-admin-password
unset ADMIN_NEW_PASSWORD

# the API on its own
dotnet run --project backend/src/SportsBooking.Api -- set-admin-password
```

In PowerShell: `$env:ADMIN_NEW_PASSWORD = Read-Host -MaskInput "New password"`, then the same command, then
`Remove-Item Env:ADMIN_NEW_PASSWORD`. After it, the admin signs in again with the new password (a lockout caused by the old
one is cleared too). The `AdminSeed` settings do not change the password of an existing admin, so update them if you keep them
in `.env`, or the next fresh installation will use the old value.

## Demo data

`seed-demo` fills an **empty** development database with realistic demo data, so there is something to show and the
admin's numbers (US24 to US28) mean something: 5 owners and 10 clubs around Porto and Lisboa (all four sports), 60
players, and about 12,000 bookings from the last 60 days and the next 14 (busier in the evening and at weekends, with
cancellations, no-shows and customer bookings), a few blocks. It is deterministic: the same day gives the same data.

```
# the API on its own (needs the database: docker compose up -d postgres)
dotnet run --project backend/src/SportsBooking.Api -- seed-demo

# with docker compose running (the API container applies the migrations first)
docker compose run --rm api seed-demo
```

It runs only with exactly that argument, exits when it is done (it never starts the web server and sends no email), and
refuses (exit code 1) in the Production environment and when any owner or player already exists. To start over, drop the
database (`docker compose down -v`) and run it again.

Every account has the same **development-only** password, `demo-password-1` (never use it anywhere real). The
addresses are on the reserved domain `example.test`:

| Who | Email |
| --- | --- |
| Owners | `dono01@example.test` … `dono05@example.test` |
| Players | `jogador01@example.test` … `jogador60@example.test` |

## Emails

Booking confirmations and cancellations (US22) are written to the `outbox_messages` table in the same transaction as
the booking, and a background worker inside the API sends them over SMTP. In development they go to Mailpit: open
http://localhost:8025 to read them. Running the API outside Docker uses `localhost:1025`
(`appsettings.Development.json`); `docker compose` sets `Email__Host=mailpit`.

Settings (section `Email`, checked at start-up): `Host`, `Port`, `Security` (`None`, `StartTls` or `SslOnConnect`),
`Username` and `Password` (both or neither), `FromAddress`, `FromName`, `TimeoutSeconds`. In production set them as
environment variables (`Email__Host`, …; Brevo: port 587, `StartTls`). The worker's own settings are in `Outbox`
(`Enabled`, `BatchSize`, `PollIntervalSeconds`, `FirstRetryDelaySeconds`, `MaximumRetryDelaySeconds`, `MaxAttempts`).
Delivery is at least once. Addresses, email bodies and SMTP credentials are never logged.

## Tests and checks

```bash
# Backend: unit + integration tests. Integration tests start PostgreSQL with Testcontainers, so Docker must be running.
dotnet test backend/SportsBooking.slnx

# Frontend
cd frontend
npm run lint
npm run typecheck
npm test
npm run build
```

## Database migrations

From `backend/` (`dotnet-ef` is a local tool, restored with `dotnet tool restore`):

```bash
dotnet ef migrations add <Name> \
  --project src/SportsBooking.Infrastructure \
  --startup-project src/SportsBooking.Api \
  --output-dir Persistence/Migrations
```

Never edit a migration that has already been applied; add a new one instead.

## API types

The backend writes its OpenAPI document on every build to `backend/src/SportsBooking.Api/openapi/sportsbooking-api.json`. The frontend generates TypeScript types from it:

```bash
dotnet build backend/SportsBooking.slnx
cd frontend && npm run gen:api   # writes src/lib/api/schema.d.ts
```

Commit both files. CI fails if either is out of date.

## Continuous integration

[.github/workflows/ci.yml](.github/workflows/ci.yml) runs on every push and pull request:

- **Backend:** build (warnings are errors) → unit and integration tests → OpenAPI and EF model drift checks → vulnerable NuGet packages
- **Frontend:** lint → typecheck → tests → build → API types drift check → `npm audit` of production dependencies

To make a red CI block merges, protect `main` on GitHub: **Settings → Branches → Add rule** for `main`, enable *Require a pull request before merging* and *Require status checks to pass*, and select the `Backend (build + tests)` and `Frontend (lint, typecheck, tests, build)` checks.

## Repository layout

```text
backend/
  src/SportsBooking.Domain/          entities and business rules, no dependencies
  src/SportsBooking.Application/     use cases, DTOs, validators, interfaces
  src/SportsBooking.Infrastructure/  EF Core, migrations, Identity, JWT sessions, (later) email, outbox
  src/SportsBooking.Api/             controllers, auth cookies, Origin check, ProblemDetails, DI composition
  tests/SportsBooking.UnitTests/
  tests/SportsBooking.IntegrationTests/
frontend/
  src/app/(player)/  manage/  admin/   the three areas
  src/app/register/                    player registration (US01)
  src/features/<feature>/              components, hooks, schemas per feature (e.g. auth)
  src/components/ui/                   shadcn/ui components
  src/lib/api/                         generated API types and the browser API client
docs/                                  backlog and stack documents
docker-compose.yml
```
