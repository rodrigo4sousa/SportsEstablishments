# 0009 · Deployment target

- **Status:** Accepted on 2026-11-09, after the first real deploy.
- **Date:** 2026-11-09 (rewritten; the first version, from 2026-10-30, proposed the API in a private Render service).
- **Elements:** C-WEB, C-API, C-WORKER, C-DB, X-GHA, PL-VERCEL, PL-VERCEL-EDGE, PL-RENDER, PL-RENDER-EDGE, PL-RENDER-API, PL-SUPABASE, PL-SUPAPOOL, PL-BREVO, PL-DBUSERS, PL-MIGBUNDLE, PL-DEPLOY, PL-DEPLOYHOOK, PL-PRODDATA. Details in the [physical view](../4plus1/physical.md).

## Context

T06 asks for a public URL with HTTPS, automatic deploy after each merge to `main` with green CI, secrets only in environment variables, migrations applied on deploy and two database users. The application is a portfolio demo that will be shut down later: **the cost has to be zero**.

What the code imposes on any target:

- **A single origin.** The session lives in httpOnly cookies and the API checks the `Origin`: the browser has to see the pages and `/api/*` at the same address.
- **The email worker** (C-WORKER) runs inside the API; if the API stops, emails stop.
- **PostgreSQL with `btree_gist`** for `bookings_no_overlap`, transactions with `FOR UPDATE` and `SKIP LOCKED`, and the Data Protection keys in the database (nothing needs local disk).
- **SMTP** for Brevo.

Alternatives considered:

1. **A virtual machine with Caddy** (the Stack's). It has a fixed cost and means administering the system, the certificates and the backups.
2. **Both on Render, with a private API** (the first version of this ADR). Private services have no free plan, so it does not meet the zero cost.
3. **Vercel (front end) + free Render (public API).** Zero cost. The API gets a public URL, and Next.js stays the same-origin proxy.

## Decision

Decision for T06: **option 3.**

- **Vercel Hobby** (PL-VERCEL): the site. The browser only talks to this origin; the `/api/*` rewrite forwards to the API and is fixed at build time (`API_INTERNAL_URL`). That is why the cookies are first-party and **there is no CORS and no Caddy**.
- **Free Render** (PL-RENDER-API): the API with the worker, in a public Docker web service, with automatic deploy turned off.
- **Free Supabase** (PL-SUPABASE): PostgreSQL, always through the pooler in session mode (port 5432, PL-SUPAPOOL). The direct host is IPv6-only and GitHub's runners have no IPv6; transaction mode does not serve the API's transactions.
- **Brevo** (PL-BREVO): SMTP, with a sender verified by email, because there is no domain of our own.
- **GitHub Actions** (X-GHA): `deploy.yml` (PL-DEPLOY) runs after green CI on `main`, in this order: migrations, API deploy through a deploy hook (PL-DEPLOYHOOK), site deploy with the Vercel CLI, smoke. `prod-data.yml` (PL-PRODDATA) runs the migrations, `set-admin-password` and `seed-demo` by hand, because Render's free plan has no shell or one-off jobs.
- **Two roles** (PL-DBUSERS): `postgres` for the migrations and the commands, `sportsbooking_app`, without DDL, for the API ([`supabase-roles.sql`](../../deployment/supabase-roles.sql)).

The manual steps are in the [runbook](../../deployment/runbook.md).

## Conditions and risks

| Topic | Consequence | What we do |
| --- | --- | --- |
| **Cold start** | After 15 minutes without traffic, the API goes to sleep and the first request takes about a minute. | Accepted in a demo. |
| **Worker stopped while the API sleeps** | Emails and reminders only go out while the API is awake; a reminder whose booking has already started is skipped. | Accepted. The outbox keeps everything, and bookings and cancellations never depend on sending. |
| **Public API and best-effort per-IP limits** | Whoever connects directly to the API can forge `X-Forwarded-For`. | Identity's account lockout as the real protection; the `Origin` still protects the browser. `ForwardLimit=1`, confirmed on the first deploy. |
| **Vercel rewrite timeout** | 120 seconds per request. Enough for a cold start of about a minute, but a very slow API returns an error. | Accepted. |
| **Free plan limits and suspensions** | Render: 750 hours per month; with no payment method, exceeding the bandwidth suspends the free services until the next month. Vercel Hobby: non-commercial use only. Supabase: 500 MB. | No payment method on Render, so there is never a charge. A suspension is acceptable. |
| **Supabase paused for inactivity** | A free project with little database activity for 7 days is paused, and the API no longer has a database. | **Mitigated, not eliminated:** the `keepalive.yml` workflow makes a read query against the database through the site (`GET /api/localities`) twice a week, which also wakes the API. Supabase's documentation does not give the "little activity" threshold and speaks of a few requests per day, so the frequency is provisional. If the project is paused, reactivate it in the dashboard. `/health` does not touch the database, so it does not count. |
| **Scheduled workflows turned off by GitHub** | In a public repository, GitHub disables scheduled workflows when there has been no repository activity for 60 days; the documentation does not define "activity". `keepalive.yml` may stop without warning, and Supabase pause again. | Re-enable in Actions → Keepalive → *Enable workflow* (or `gh workflow enable keepalive.yml`). See the [runbook](../../deployment/runbook.md#10-keep-the-demo-alive). |
| **No domain of our own** | Brevo uses only a sender verified by email, with no DKIM or DMARC for the domain; emails may land in spam. | Accepted in a demo. |
| **Migrations before the API** | The new schema goes live before the new API. | **Every migration must work with the previous version of the API** (add before removing). |
| **Supabase API over `public`** | The tables have no row level security and `users` holds emails and hashes. | `supabase-roles.sql` removes the privileges of `anon`, `authenticated` and `service_role`. |
| **No automatic backups** | The free plan does not include them. | Accepted: the data is demo data. |

## What the first real deploy confirmed (2026-11-09)

According to the project owner, who did the first deploy.

**Confirmed**

- `ForwardedHeaders__ForwardLimit=1` gave the right client IP.
- Brevo: SMTP on port 2525 with StartTls worked.
- Login and a request that changes data through the Vercel rewrite worked, with the `Origin` and the `Set-Cookie`.
- Caching in the Vercel rewrites: nothing was cached.
- Render: the Dockerfile, the build context and the health check used were those in the [runbook](../../deployment/runbook.md).

**Corrected:** nothing. There were no differences from what the documentation said.

**Still open** (not reported, so still provisional in the [backlog](../../backlog.md))

- The client IP on Server Component calls: the value 1 was confirmed, but the reported result does not distinguish them.
- The PostgreSQL version Supabase gives to new projects.
- The behaviour of `/health` during a build (it confirms the API answers, not that the new commit is already active) and the Vercel CLI from the repository root.

## Consequences

**Good**

- Zero cost, with no machines, certificates or operating system to administer.
- The same origin as development: the cookies stay first-party and there is no CORS.
- The deploy order is explicit and controlled by a single workflow.

**Bad or to watch**

- The API has a second public entry point (`*.onrender.com`).
- The request crosses two providers over the public Internet, which adds latency.
- `/health` does not say which version is running: during a build, Render keeps serving the previous instance, so the site may be published before the new API is active.
- Changing the API's address requires a new deploy of the site (the rewrite is fixed at build time).
- Four provider accounts and several secrets.

## What this ADR replaces in the Stack

It replaces, without editing the [Stack](../../stack.md):

- **Section "Architecture":** the sentence "The browser only talks to Caddy".
- **Section "Stack", row "Infrastructure":** Caddy as the proxy with HTTPS.
- **Section "Authentication: httpOnly cookies", paragraph "Same origin, mandatory":** "Caddy (in production)". In production, the same origin comes from the Next.js rewrites. The sentence about Vercel is still true as far as it goes (front end and API on different domains, called directly by the browser, give third-party cookies); here the browser never calls the API directly.
- **Section "Repository, Git, CI and deploy", part "Deploy" and the repository tree:** the virtual machine with docker compose, Caddy, the `Caddyfile`, the GitHub Container Registry, deploy over SSH and the Azure Container Apps alternative.

It also replaces the first version of this ADR: the API in a private Render service, with Next.js on Render and the private network.

These stay as they are: the migration bundle and the two database users (T06 criteria) and the rest of the Stack.

The ids PL-CADDY, PL-HOST, PL-GHCR, PL-RENDER-WEB, PL-RENDER-NET and PL-DNS stay in the inventory, marked "not adopted".
