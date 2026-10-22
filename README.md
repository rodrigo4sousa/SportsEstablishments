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

The browser only talks to the app. Next.js rewrites `/api/*` to the API, so frontend and API share one origin and the auth cookies stay first-party. EF Core migrations are applied automatically when the API starts in Development.

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

Set `API_INTERNAL_URL` to point the frontend at a different API address.

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

If the settings are missing or invalid, the API starts without an admin and logs a warning or error. The seed never changes an existing admin (changing the password setting later has no effect), never creates a second admin and never promotes an existing account.

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
  src/SportsBooking.Infrastructure/  EF Core, migrations, (later) Identity, JWT, email, outbox
  src/SportsBooking.Api/             controllers, ProblemDetails, DI composition
  tests/SportsBooking.UnitTests/
  tests/SportsBooking.IntegrationTests/
frontend/
  src/app/(player)/  manage/  admin/   the three areas
  src/components/ui/                   shadcn/ui components
  src/lib/api/                         generated API types
docs/                                  backlog and stack documents
docker-compose.yml
```
