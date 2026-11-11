# Production environment variables (T06)

Target: **Next.js on Vercel** (Hobby), **API in Docker on Render** (free plan), **PostgreSQL on Supabase**, **SMTP on Brevo** and **GitHub Actions** for the deploy steps. The browser only talks to Vercel; Next.js rewrites `/api/*` to the API, so the cookies are first-party and there is no CORS.

Only example shapes appear here, **never real values**. One table per place. The manual steps are in the [runbook](runbook.md). Checked on 2026-11-09; what depends on a provider is in the last section, with what was read, what the first real deploy confirmed and what is still to be confirmed.

## What "read at build time" means

`API_INTERNAL_URL` is read twice, at different moments:

- **At build time** (`next build`, in `next.config.ts`): the destination of the `/api/*` rewrite is **written into the build output**. Changing the variable does not alter a deploy that already exists: a **new deploy** is needed.
- **At run time** (`lib/server/api-url.ts`, server-only code): Server Components call the API with the user's cookie. It is not `NEXT_PUBLIC_`, so it never reaches the browser.

On Vercel it is the same variable for both moments. The build fails on purpose if it is missing, is not a plain origin (no path or trailing slash) or is not `https`. That check uses the `VERCEL` system variable, so the project's "Automatically expose System Environment Variables" option has to stay on.

When the build refuses the value, the error shows what it received, in quotes and with invisible characters written as `\uXXXX`, and the reason (empty, trailing slash, path, wrong scheme, spaces, quotes, invisible characters). The origin is not a secret, so it may appear in the build log.

`ENABLE_HSTS` is also read only at build time: the headers are written into the output.

## Vercel

The project's Root Directory: `frontend`.

| Name | When it is read | What it is for | Example shape | Where it comes from |
| --- | --- | --- | --- | --- |
| `API_INTERNAL_URL` | Build and run time | Destination of the `/api/*` rewrite and address used by Server Components | `https://<service>.onrender.com` | Public URL of the service on Render, without a trailing slash |
| `ENABLE_HSTS` | Build | Turns on HSTS and `upgrade-insecure-requests` on the site served over HTTPS | `true` | Choice; only with HTTPS |

After changing either one, a new deploy is needed. Vercel previews have another address, which is not in `Security__AllowedOrigins__*`: requests that change data get 403 `origin_not_allowed`.

## Render

A Docker-type web service, with `backend/Dockerfile` and `backend` as the context. Render passes the environment variables as build arguments; the `Dockerfile` uses none, so no secret ends up in the image. The API does not apply migrations at startup in Production.

| Name | Required | What it is for | Example shape | Where it comes from |
| --- | --- | --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | Yes | Turns on the production rules | `Production` | Fixed |
| `PORT` | Given by Render | Port the API listens on, on `0.0.0.0` (`8080` if absent) | `10000` | Render |
| `ConnectionStrings__Default` | **Yes, the API does not start without it** | The API's connection to the database, as `sportsbooking_app`, without DDL rights (secret) | `Host=<pooler>;Port=5432;Database=postgres;Username=sportsbooking_app.<ref>;Password=<…>;SSL Mode=Require;Maximum Pool Size=5` | Supabase, session mode (port 5432), never transaction mode. Npgsql format, not `postgresql://`. The pool is small because the free plan has 15 connections |
| `Jwt__SigningKey` | **Yes, at least 32 bytes** | Signs the access tokens (secret) | `<48 random bytes in base64>` | `openssl rand -base64 48`; for this environment only |
| `Security__AllowedOrigins__0` | **Yes, the API does not start without it** | The only origin allowed to POST, PUT, PATCH and DELETE | `https://<project>.vercel.app` | The site's public address, without a trailing slash |
| `PublicSite__BaseUrl` | **Yes** | The site's address in the emails' links | `https://<project>.vercel.app` | Same as the previous one |
| `ForwardedHeaders__TrustAnyProxy` | Yes, for the per-IP limits to work | Trusts `X-Forwarded-For` and `X-Forwarded-Proto` coming from any connection (see below) | `true` | Fixed for this target |
| `ForwardedHeaders__ForwardLimit` | No (1) | How many entries are read, from right to left | `1` | Confirmed on the first deploy (2026-11-09); see "Finding the `ForwardLimit`" if you change provider |
| `AdminSeed__Email`, `AdminSeed__Password` | Do not set here | The admin is created by `prod-data`. If set, the API would need the database to start | — | Without them, the API warns in the log and starts |
| `Email__Host` | With `Email__FromAddress`, or neither | SMTP server | `smtp-relay.brevo.com` | Brevo |
| `Email__Port` | No (587) | SMTP port | `2525` | Worked on the first deploy. Render's free plan blocks outbound ports 25, 465 and 587 |
| `Email__Security` | No (`StartTls`) | `StartTls`, `SslOnConnect` or `None` | `StartTls` (worked on port 2525 on the first deploy) | Never `None` with real credentials |
| `Email__Username`, `Email__Password` | Both or neither | SMTP credentials (secret: the SMTP key, not the API key) | `<SMTP login>`, `<SMTP key>` | Brevo, "SMTP & API" |
| `Email__FromAddress` | With `Email__Host`, or neither | Sender | `reservas@<verified domain>` | Sender verified in Brevo |
| `Email__FromName`, `Email__TimeoutSeconds` | No | Sender name; limit for each send | `Reservas de Campos` (Pitch Bookings), `30` | Optional |
| `RateLimiting__Auth__*`, `RateLimiting__Public__*` | No | Per-IP limits | `RateLimiting__Public__ReadPermitLimit=120` | Optional (provisional values) |
| `Outbox__*` | No | Email worker | `Outbox__PollIntervalSeconds=10` | Optional |

**Do not set on Render:** `ADMIN_NEW_PASSWORD` and `DemoData__AllowInProduction` (only for the command's process, below). `Database__ApplyMigrationsOnStartup` is ignored in Production.

**Without SMTP** (`Email__Host` and `Email__FromAddress` both empty), the API starts in Production, warns in the log and sends nothing; the messages stay in the outbox until SMTP exists. Only one of the two set, or an invalid value, prevents startup. Bookings and cancellations never depend on sending.

## GitHub Actions (secrets)

The commands run on the runner against Supabase, because Render's free plan has no shell or one-off jobs. They start the same host as the API, so the required settings have to exist: the workflows provide them, and none of them is an additional secret.

| Name | Used by | What it is for | Example shape | Where it comes from |
| --- | --- | --- | --- | --- |
| `DB_OWNER_CONNECTION_STRING` | `deploy.yml`, `prod-data.yml` | Connection as `postgres`, for the migrations and the commands (secret) | `Host=<pooler>;Port=5432;Database=postgres;Username=postgres.<ref>;Password=<…>;SSL Mode=Require` | Supabase, session mode (port 5432). The runner has no IPv6, so never the direct host |
| `RENDER_DEPLOY_HOOK_URL` | `deploy.yml` | Launches the API deploy (secret) | `https://api.render.com/deploy/srv-<…>?key=<…>` | Render |
| `VERCEL_TOKEN`, `VERCEL_ORG_ID`, `VERCEL_PROJECT_ID` | `deploy.yml` | Publish the site with the CLI (secrets) | `<token>`, `team_<…>`, `prj_<…>` | Vercel |
| `ADMIN_EMAIL`, `ADMIN_PASSWORD` | `prod-data.yml` (`set-admin-password`) | Create the admin, if it does not exist, and set its password, always through the environment (secrets) | `admin@<domain>`, `<password ≥ 8>` | Choice |

Repository variables (not secrets):

| Name | What it is for | Example shape |
| --- | --- | --- |
| `DEPLOY_ENABLED` | Without the value `true`, the two workflows do nothing | `true` |
| `RENDER_API_URL` | Where the workflow confirms `/health` | `https://<service>.onrender.com` |
| `SITE_URL` | The smoke test and the commands' startup settings | `https://<project>.vercel.app` |

Defined in the workflow itself: `ASPNETCORE_ENVIRONMENT=Production`, `Security__AllowedOrigins__0` and `PublicSite__BaseUrl` (from `SITE_URL`), `Jwt__SigningKey` (generated on each run and never used) and, only in the `seed-demo` step, `DemoData__AllowInProduction=true`. The migrations read `ConnectionStrings__Default` from the environment, so the connection never appears on a command line.

Without `DemoData__AllowInProduction=true`, `seed-demo` refuses in Production (exit code 1); with it, it still refuses a database that already has owners or players. The demo accounts have the README's public password: anyone can sign in with them while the demo is online.

## Finding the `ForwardLimit`

**Provisional.** The API is public on Render, so whoever connects to it directly can send their own `X-Forwarded-For`: the per-IP limit is best-effort only, and the real protection against guessing passwords is Identity's account lockout (5 attempts, 15 minutes).

How many entries reach the API depends on Vercel, Cloudflare and Render's load balancers, and that is not documented. To find out, after a new deploy:

1. With `ForwardedHeaders__ForwardLimit=1`, make more than 10 logins in a minute, on the site, from your own connection, with an email that does not exist (so as not to lock any account), until you get 429.
2. Look in the Render log for the line `Too many requests from client <address> to /api/auth/login`.
3. If the address is that of your own connection, it is right. If it is another (Vercel's, for example), raise the limit by 1 and repeat, with a new deploy.
4. Use the lowest value that gives the right address: the higher it is, the easier it is to forge.

**Result of the first deploy (2026-11-09):** the value that gave the client's right IP was `1`.

## Known limits of this configuration

- **Free Render:** the service goes to sleep after 15 minutes without traffic and takes about a minute to wake up. While it sleeps, the email worker stops: emails go out when someone wakes the API, and a reminder whose booking has already started is skipped.
- **First request after sleeping:** Vercel waits at most 120 seconds for an external rewrite.
- **Database down:** `/health` only says the process is alive and does not touch the database. But with `AdminSeed__*` set, the API reads the database at startup and does not start if it does not answer.

## What was confirmed with the providers (2026-11-09)

**Read in the current documentation:**

- **Vercel** ([Request headers](https://vercel.com/docs/headers/request-headers), [Rewrites](https://vercel.com/docs/rewrites), [Limits](https://vercel.com/docs/limits), [Environment variables](https://vercel.com/docs/environment-variables)): rewrites `X-Forwarded-For` with the visitor's public IP; on an external rewrite it sends the origin `x-forwarded-for`, `x-real-ip`, `x-forwarded-host` and `x-forwarded-proto`; 120-second limit; changing a variable only takes effect on a new deploy; `VERCEL` exists at build time and at run time, if system variables are exposed.
- **Render** ([Free](https://render.com/docs/free), [Web services](https://render.com/docs/web-services), [Docker](https://render.com/docs/docker), [Health checks](https://render.com/docs/health-checks), [DDoS article](https://render.com/articles/how-render-handles-ddos-attacks)): sleeps after 15 minutes, wakes in about a minute; ports 25, 465 and 587 blocked outbound; no shell or one-off jobs; `PORT` (default 10000) and `0.0.0.0`; traffic goes through Cloudflare and Render's load balancers, and the application should read `x-forwarded-for`; environment variables become build arguments.
- **Brevo** ([SMTP integration](https://developers.brevo.com/docs/smtp-integration)): ports 587 and 2525, and 465 with SSL/TLS; use an SMTP key.

- **Supabase** ([Connecting to Postgres](https://supabase.com/docs/guides/database/connecting-to-postgres), [IPv4](https://supabase.com/docs/guides/platform/ipv4-address), [Securing the Data API](https://supabase.com/docs/guides/api/securing-your-api)): the direct host is IPv6, or IPv4 with a paid add-on; the shared pooler is IPv4; session mode uses port 5432 and supports prepared statements; the user is `<role>.<project-ref>`; `sslmode=require` is the recommended setting.

**Confirmed on the first real deploy (2026-11-09):**

- `ForwardedHeaders__ForwardLimit=1` gave the client's right IP.
- Brevo: SMTP on port 2525 with `StartTls` worked.
- Login and a request that changes data through the Vercel rewrite worked, with the `Origin` and the `Set-Cookie`.
- Vercel's caching on the rewrites: nothing was cached.
- Render: the Dockerfile, the build context and the health check used were those in the runbook.
- Nothing else differed from what the documentation said.

**Not reported (still to be confirmed):**

- `X-Forwarded-For` on the Server Components' calls on their way to Render: the value `1` was confirmed, but the reported result does not distinguish them.
- The PostgreSQL version Supabase gives to new projects.
