# 0001 · A single application with three areas

- **Status:** Accepted.
- **Date:** 2026-10-30 (record; the decision comes from the [Stack](../../stack.md), dated 2026-10-22).
- **Elements:** C-WEB, C-API, WEB-AREA-PLAYER, WEB-AREA-MANAGE, WEB-AREA-ADMIN, WEB-AREA-ACCOUNT, WEB-GUARDS, WEB-PROXY, CC-AUTHZ.

## Context

The product has three profiles with very different screens: the player searches and books, the owner manages clubs and the admin sees numbers ([backlog](../../backlog.md), "Profiles"). All of them talk to the same API, which is the sole owner of the business rules, and an account has a single profile.

Alternatives:

1. **One Next.js application per profile** (three projects and three deploys). It isolates the code of each profile, but repeats the API client, the login, the forms and the configuration, and means maintaining three builds.
2. **One application for the public and another for management** (owner and admin). Half the duplication, and the same problem.
3. **A single application with the three areas by route.**

## Decision

Option 3: **a single Next.js app (C-WEB)** with the areas by route prefix on top of **a single API (C-API)**: `/` and `/clubs/[slug]` (player, public), `/manage` (owner), `/admin` (admin), plus the account pages (`/login`, `/register`, …). The code lives in `frontend/src/app/` (one route group per area) and `frontend/src/features/` (one folder per feature).

Access to each area is decided at two levels:

- **On the Next.js server, for convenience:** the layouts of `/manage`, `/admin` and `/bookings` have a guard (`AreaGuard`, `PlayerGuard`) that redirects to `/login?next=…` or to the right profile's area; `proxy.ts` only writes the requested path to `x-pathname` for this.
- **In the API, which always decides:** an authorization policy per profile (`Player`, `Owner`, `Admin`) and, for the owner, the per-club check (`OwnedClubs`, T02). Another profile gets 403; a resource of another owner gets 404.

## Consequences

**Good**

- One login, one API client (`lib/api/client.ts`), one set of components and generated types, one build and one deploy.
- A single origin for the browser, which the cookie design needs ([ADR 0002](0002-session-in-httponly-cookies.md)).
- The guards are only a convenience: if they fail, the API still refuses.

**Bad or to watch**

- The code of the three areas ships in the same package: the separation is by folders and by convention, not enforced by projects.
- The guards read cookies, so they sit inside `<Suspense>` because of `cacheComponents`; a redirect is then sent as `<meta http-equiv="refresh">` and not as a 307 ([process view](../4plus1/process.md#27-page-of-a-guarded-area-rendered-on-the-server)).
- Whoever changes a shared feature (for example `formatPrice`) touches several areas.
