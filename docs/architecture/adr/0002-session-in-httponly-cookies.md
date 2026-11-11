# 0002 · Session in httpOnly cookies instead of localStorage

- **Status:** Accepted.
- **Date:** 2026-10-30 (record; the decision comes from the [Stack](../../stack.md), "Authentication: httpOnly cookies", and from US03).
- **Elements:** C-BROWSER, C-WEB, C-API, API-AUTH, CC-COOKIES, CC-ORIGIN, CC-RATE, WEB-API-CLIENT, WEB-GUARDS, D-REFRESH.

## Context

The application needs to know who is signed in on every request, and the page's JavaScript must not be able to steal the session if there is an XSS attack. The front end has parts on the server (Server Components, which call the API) and parts in the browser.

Alternatives:

1. **Tokens in `localStorage` or `sessionStorage`**, sent in `Authorization: Bearer`. Any injected script reads them; Server Components do not see them.
2. **A server-side session** (an opaque identifier in a table, no JWT). It would solve the previous point, but requires a database round trip on every request.
3. **A short-lived JWT and a rotating refresh token, both in httpOnly cookies.**

## Decision

Option 3 ([Stack](../../stack.md)):

- `access_token`: an HS256 JWT with `sub` and `role`, 15 minutes, `HttpOnly; Secure; SameSite=Lax; Path=/`. JwtBearer reads it from the cookie.
- `refresh_token`: 32 random bytes stored only as a SHA-256 (`refresh_tokens`), 7 days of absolute lifetime, `HttpOnly; Secure; SameSite=Strict; Path=/api/auth`. Each refresh rotates the token through a conditional update; an already used token that shows up again (on refresh or on logout) revokes all the account's sessions.
- The browser knows who the user is through `GET /api/auth/me`; it never reads the tokens.
- **Same origin, no CORS:** the pages and `/api/*` come from the same address (today, the Next.js rewrites), so the cookies are first-party. The API refuses `POST`/`PUT`/`PATCH`/`DELETE` whose `Origin` is not in `Security:AllowedOrigins` (403 `origin_not_allowed`), which, together with `SameSite`, protects against CSRF.
- Against brute force: Identity's account lockout (5 failures, 15 minutes) and per-IP request limits on the authentication routes.

## Consequences

**Good**

- An XSS does not read the tokens.
- Server Components pass the user's cookie to the API; there is no second mechanism for the server.
- A deactivated account stops renewing its session and its sessions are revoked.

**Bad or to watch**

- **The server never renews the session**, because the refresh cookie only goes to `/api/auth` and pages do not carry it. Refreshes always come from the browser: a 401 triggers a single refresh and repeats the request, with a Web Lock between tabs (otherwise the second tab would present an already used token and the account would lose all its sessions), and `/login` tries a silent refresh before the form.
- **Accepted trade-off:** the access token is validated without going to the database, so it stays valid for up to 15 minutes after the account is deactivated or the password changes.
- Calls made by the server that change data (Route Handlers, Server Actions) would have to send the site's `Origin`; today there are none.
- Cookie security depends on a single origin: the production design ([ADR 0009](0009-deployment-target.md)) has to keep it.
