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

There is no endpoint for it, and the seed never touches an existing admin. To change the admin password (or email) by hand:

1. Stop the API.
2. Delete the admin account. Its profile link (`user_roles`) is deleted with it:

   ```bash
   docker compose exec postgres psql -U sportsbooking -d sportsbooking -c \
     "DELETE FROM users WHERE id IN (SELECT ur.user_id FROM user_roles ur JOIN roles r ON r.id = ur.role_id WHERE r.normalized_name = 'ADMIN');"
   ```

3. Set the new `AdminSeed` values and start the API. The seed creates the admin again, with a new id.

This works while nothing else references the admin account. Revisit it when admin actions start being recorded (US29).

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
