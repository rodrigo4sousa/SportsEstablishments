# Backlog — Pitch Booking App
 
Oct 22, 2026 · @Rodri
 
The MVP adds up to 92 points (19 user stories and 6 technical ones); what is left for later adds up to 45. A single Next.js (React) app with three areas: `/` (player), `/manage` (owner) and `/admin` (admin), on top of an ASP.NET Core API. The interface is in Portuguese (pt-PT) and, since T09, in English; on-screen texts are quoted in Portuguese, followed by their English meaning in parentheses.
 
## Points to review in T06

- Request limit of the public read endpoints: it stays at 120 requests per minute per IP (`RateLimiting:Public`), to be reviewed in T06.
- **T06 target (decided by the project owner, replaces the Stack's virtual machine with Caddy; ADR 0009, rewritten, was accepted on 2026-11-09):** Next.js on Vercel, API in Docker on Render's free plan, PostgreSQL on Supabase, SMTP on Brevo, GitHub Actions. The variables are in `docs/deployment/environment-variables.md`.
- **Provisional (client IP):** the API is public on Render and the addresses of its proxies cannot be listed, so in production it uses `ForwardedHeaders__TrustAnyProxy=true`, with an explicit `ForwardedHeaders__ForwardLimit` (1). Whoever connects directly to the API can forge `X-Forwarded-For`, so **the per-IP limit is best-effort only and the real protection against guessing passwords is Identity's account lockout**. **Closed:** `ForwardLimit=1` gave the client's right IP on the first deploy (2026-11-09). The rest stays provisional: the per-IP limit is best-effort, and the IP on Server Component calls was not reported.
- **Provisional (SMTP in production):** without `Email__Host` and `Email__FromAddress`, the API starts in Production, warns in the log and sends nothing; the messages stay in the outbox until SMTP exists. It replaces, in Production only, the rule of US22 and T06 "without them the API does not start". One of the two alone, or an invalid value, still prevents startup.
- **Closed (Brevo):** SMTP on port 2525 with `StartTls` worked on the first deploy (2026-11-09); port 2525 is the one that works, because Render's free plan blocks outbound ports 25, 465 and 587.
- **Provisional (migrations):** in Production the API never applies them at startup, even with `Database__ApplyMigrationsOnStartup=true`; that setting only counts outside Production. The new schema goes live before the new API, so **every migration must work with the previous version of the API**.
- **Provisional (Vercel CLI):** `deploy.yml` runs the CLI from the repository root and not from `frontend`. With the project's Root Directory set to `frontend`, the CLI starting from `frontend` looks for `frontend/frontend` and fails (tested with the pinned version).
- **Provisional (waiting for the API):** `/health` does not say which version is running and, during a build, Render keeps serving the previous instance. A 200 confirms the API answers, not that the new commit is already active; the site may be published before that.
- **Provisional (Supabase keepalive):** the frequency (twice a week) and what counts as activity for Supabase and for GitHub are not defined in numbers in the documentation; see the note under T06.
- **Provisional (PostgreSQL version):** the tests run on 18.6 and the Supabase pages read do not say which version it gives to new projects. The migrations and `seed-demo` were also run on 17.

## Architecture diagrams

- **Provisional (`process-runtime` and `logical-layers`):** the request to align the diagrams with the Pokédex's says to turn into sequence diagrams all those that include C4 outside `c4/`, but these two are not sequences: they show processes and dependencies, with no order in time. They stayed as UML component diagrams, with the same boxes and arrows, with no C4 include.

## UX improvements (audit)

The UX and accessibility audit (`docs/ux-audit.md`, 30 findings in four batches) is not a story: the fixes go in their own `feat/ux-batch-N-...` branches, without changing business rules or the API. The status of each finding is in section 9 of the report.

- **Batch 1, foundations: done.** Visible focus, contrast in the tokens, focus after actions, 404 and error pages in pt-PT, titles, skeletons, empty states, toast for unexpected errors. The findings that were left for other batches are listed in the report.
- **Batch 2, player flow: done** (report, section 9): the club page with the times first and, on mobile, a bar with the main action (`position: sticky`, no dependencies), the search results as whole links, the booking list by day, the mobile header with a `Conta` (Account) menu, the wait time after a 429 (`Retry-After`), the introduction on the home page, and the legend of the times. The club page's text says `por horário` (per time) (and not "per slot") next to `horário`; the owner's screens started saying `horário` in batch 3.
- **Batch 3, owner area: done** (report, section 9): the `Conta` menu also in the owner's header, `Calendário` (Calendar), `Estatísticas` (Statistics) and `Editar` (Edit) shortcuts on each club in `/manage`, the calendar on a wider page, one pitch at a time on mobile (`Campo` (Pitch) selector), the booking details beside the grid with Escape and the focus back on the box, box names equal to the visible text, statistics as cards on mobile, the club's name on the pages, form focus, and `horário` instead of "slot" on the owner's screens (the API field is still `pricePerSlot`). **Decided after batch 2:** the height of bookings on mobile stays as it is (215 px) and there is no contacts column on desktop.
- **Batch 4, admin and final pass: done** (report, section 9 and "Closing"): admin charts with series at 3:1 and numbers above the bars, each chart's data in a table anyone can open, the admin's tables as cards on mobile, owner names as links, the owner page with skeletons, the fixed hours column and flexible columns in the calendar, the club's and owner's name in the tab titles, and the accessibility pass on all profiles (200 % zoom, reflow at 320 px, keyboard, reduced motion). The application has no dark theme (the `.dark` tokens exist, but nothing applies the class): none was added. Left out, to decide: dark theme, brand colour, test with a screen reader and on a real phone.
- **Decided (visual direction):** neutral theme, no brand colour for now; a short introduction on the home page comes in batch 4 (UX-030).
- **Decided (navigation, batch 3):** shortcuts per club (`Calendário`, `Estatísticas`) in `/manage` and a menu in the owner's and admin's header (UX-013).
- **Decided (calendar, batch 3):** on mobile, one pitch at a time with a selector; on desktop, a wider page only for the calendar (UX-005).
- **Decided (club page, batch 2):** the slots move above the contacts; the fixed booking bar on mobile only comes in if the effort is S or M (UX-010).
- **Error fixed, US19 and other screens (UX-019):** unexpected errors (500, network failure, anything that is not a 4xx response from the API) went to a message on the page, sometimes out of view, instead of the toast that AGENTS.md describes (`isExpectedApiError` and `unexpectedErrorMessage` in `lib/api/expected-error.ts`). They now go to the toast; only login, password recovery, the new password and booking still describe `sem ligação` (no connection) inside the form, on purpose.

## Conventions and business rules
 
**Format.** Each story follows "As a \<profile>, I want \<action>, so that \<benefit>", with testable acceptance criteria. **M** = MVP, **D** = after the MVP.
 
**Story points (Fibonacci: 1, 2, 3, 5, 8).** They measure relative effort and uncertainty, not hours. The reference is US02 (owner registration) = 2 points. After the first sprint, your velocity (points done per sprint) is what converts points into time. Stories of 8 points are candidates for splitting.
 
**Profiles.** Player, Owner and Admin. The admin is the platform owner: it has no public registration and sees only counts, never players' personal data.
 
**Decided rules**
 
- One email corresponds to a single account and a single profile.
- The platform is a single marketplace: one player account works at every club of every owner. There are no per-owner sites or domains.
- An owner can have several clubs; each club belongs to a single owner.
- Cancellation window defined per club, 24 hours by default. The one in force at the moment of cancelling counts.
- Bookings from today up to 30 days ahead.
- Slot duration per pitch: 30, 60 or 90 minutes.
- Bookings and cancellations are never deleted: they change state (Confirmed, Cancelled by player, Cancelled by club, No-show).
- Each booking stores the price at the moment it was made.
- Payments are out of the MVP.
- Dates stored in UTC and shown in Lisbon time.
## Epic 1 — Accounts and access

**Cross-cutting requirement for the authentication stories (US01, US03, US04).** All sessions, of any profile, are renewed by `POST /api/auth/refresh`, which always checks that the account is still active before issuing new tokens. A deactivated account does not receive new tokens, and the refresh request is treated as failed (401). There is an integration test for this case.
 
### US01 · Player registration · M · 3 points
 
As a player, I want to create an account with email and password, so that I can book pitches and manage my bookings.
 
- Registration asks for name, email, password and mobile number (optional).
- The email has to be unique; if it already exists, a message says so.
- The password has at least 8 characters and is stored hashed, never as text.
- Invalid fields are rejected with a per-field message, on the front end and in the API.
- After registration, the player is signed in and goes to the home page.
- The account created has the Player profile.
- Decided: POST, PUT, PATCH and DELETE requests without an `Origin` header, or with an `Origin` from another site, are rejected with 403 `origin_not_allowed`.
- Decided (confirmed):
  - **Email already used (accepted trade-off):** the criterion asks for a message saying so, so registration reveals that an email has an account. The API answers 409 with the code `email_taken` and always the same message (`Já existe uma conta com este email.` (An account with this email already exists.)), whether the account is a player's, an owner's or an admin's, with no other data. The comparison ignores case and surrounding spaces; the database's unique index decides simultaneous registrations. The alternative with no enumeration (always answering "check your email" and notifying by email) contradicts this criterion and the one about being signed in right after. Login (US03) and recovery (US05) still do not reveal whether the email exists. Rate limiting of the `/api/auth/*` routes is done in US03.
  - Name required, up to 100 characters; surrounding spaces are removed.
  - Email: a simple address (no display name), with a dot in the domain and up to 256 characters; surrounding spaces are removed; it is stored as typed.
  - Optional mobile number: 9 to 15 digits, with an optional leading `+`; spaces are removed. Accepts foreign numbers.
  - Password: only the minimum of 8 characters, with no maximum or composition rules; it is used as typed (spaces are not trimmed).
  - Session: the 7-day lifetime of the refresh token is absolute (rotation keeps the expiry date), so signing in again is needed, at most, 7 days after registration or login. An already used refresh token that shows up again ends all the account's sessions (docs/stack.md); tabs renew one at a time (Web Locks, US03), so as not to end each other's session.
  - Anonymous requests to routes that do not exist get 401 (fallback policy that requires a session), and not 404.
  - The admin account created by the seed is named `Administrador` (Administrator).
  - The development JWT signing key (`Jwt:SigningKey` in `appsettings.Development.json`) is in the repository and is only for development; outside it, it always comes from `Jwt__SigningKey`.
  - The front end libraries use versions published at least two weeks ago. Decided exception: `zod` 4.6.7 (published on 2026-10-12) was adopted immediately because it fixes security advisory GHSA-mrhx-c7xc-m3w5 (high severity), which made CI's `npm audit` fail; earlier versions have the advisory. All the dependencies in `package.json` have an exact version (no `^`); changing a version is always a commit of its own.
- Decided (confirmed):
  - Deactivated account (Epic 1 requirement): `users.deactivated_at` set. US29 adds the reason and the record of who deactivated.
  - The home page shows `Olá, <nome>` (Hello, <name>) in the header after registration, or the links `Entrar` (Sign in) and `Criar conta` (Create account) without a session (`Entrar` came with US03).
### US02 · Owner registration · M · 2 points
 
As a club owner, I want to create my owner account, so that I can register and manage my club on the platform.
 
- There is a registration page of its own for owners, separate from the player's.
- It asks for the same data and applies the same validations as US01.
- An email already used on another account (of any profile) is rejected.
- The account created has the Owner profile.
- After registration, the owner goes to `/manage` and is invited to create the first club.
- Status: done. The page is `/register/owner` and the endpoint is `POST /api/auth/register/owner`: the Owner profile is fixed in the endpoint and never comes from the request, so no request creates an admin. Same body, same validations, the same 409 `email_taken` for any profile and the same cookies as player registration. Player and owner registration share the same per-IP request limit.
- Decided: a user with a session who opens `/login` or `/register` (including the owner registration page) is redirected to the home page of their profile, with a check on the server.
- After registration, `/manage` shows the empty state with the `Criar o primeiro clube` (Create the first club) button, which opens the creation form (US06).
### US03 · Login and logout · M · 5 points
 
As a registered user, I want to sign in and out of my account, so that I can access my profile's area.
 
- Login with email and password; wrong credentials show a generic message, without revealing whether the email exists.
- After login, the user goes to their profile's area: `/` (player), `/manage` (owner) or `/admin` (admin).
- The admin created by the seed (US04) signs in through this login and goes to `/admin` (criterion that moved from US04 to here, with a test).
- US01 notes for this story: `POST /api/auth/refresh`, the rotation and the refusal of deactivated accounts already exist.
- Rate limiting and temporary account lockout on the `/api/auth/*` routes. This also slows down email enumeration through registration (US01).
- With no proxy configured, `X-Forwarded-For` is ignored. In production it is **provisional** (T06, "Points to review in T06"): the API believes any connection, with an explicit limit of entries, so the IP can be forged by whoever connects directly to the API.
- The session uses a short-lived JWT access token (15 minutes) and a refresh token. A request with an expired or invalid access token returns 401; the front end then calls `POST /api/auth/refresh` once and repeats the request. Only if the refresh fails is the user sent to login.
- The refresh checks that the account is still active; a deactivated account does not receive new tokens (see the Epic 1 cross-cutting requirement and US29).
- Whoever tries to open another profile's area is redirected on the front end, and the API returns 403.
- Logout ends the session in the browser: it receives the `refresh_token` cookie (which goes to all of `/api/auth`), revokes that token in the database and deletes the two cookies with the same paths they were created with.
- A deactivated owner account (US29) cannot sign in.
- Status: back end and front end done (`/login` page, redirect by profile, area guards, `Sair` (Sign out) button, refresh between tabs).
- Decided (front end): the server never renews the session (the `refresh_token` cookie only goes to `/api/auth`). The guards live in the layouts of `/manage` and `/admin`; no session → `/login?next=<path>`; wrong profile → that user's area. The login page first tries a silent refresh in the browser and, on success, goes on to `next`. `next` is only accepted if it is a relative path starting with a single `/` and belonging to the user's area; otherwise it goes to the profile's home page (player `/`, owner `/manage`, admin `/admin`). Only one tab at a time renews the session (Web Locks API).
- Decided (confirmed):
  - **Generic failure:** unknown email, wrong password, locked-out account and deactivated account all give the same 401 `invalid_credentials` (`Email ou palavra-passe incorretos.` (Incorrect email or password.)), after the same hashing work, so that the response time does not reveal whether the email exists either. A malformed email also gives this 401; only empty fields give 400 `validation_failed`, per field.
  - **Lockout:** 5 wrong passwords in a row lock the account for 15 minutes (Identity lockout). Attempts during the lockout do not extend it. A correct login restarts the count. Wrong attempts in parallel on the same account may not all be counted (Identity's concurrency control); rate limiting covers that case.
  - **Rate limiting:** fixed window of 1 minute, per client IP and per route: login 10, registration 5, refresh 30 requests (refresh has more margin because several users may share an IP). An IPv6 client counts per /64 network. Values configurable in `RateLimiting:Auth`. Above the limit: 429 `too_many_requests` with `Retry-After`.
  - **Trusted proxies:** none by default; the IP is the connection's. In production, **provisional** (T06, "Points to review in T06"): `ForwardedHeaders:TrustAnyProxy` with `ForwardedHeaders:ForwardLimit`, because Render's proxies have no addresses that can be listed. The Next.js container is never trusted: the rewrite passes on an `X-Forwarded-For` sent by the browser. In development all requests share one limit (the Next.js container's), which is enough for a developer.
  - **Public pages (front end):** `/` and the search and club pages stay public for all profiles; the guards only exist in `/manage` and `/admin`. A user with a session who opens `/login`, `/register` or `/register/owner` is redirected on the server to the home page of their profile (US02).
  - **Return path:** the guard reads the request's full path (the `x-pathname` header, set by the minimal `proxy.ts`, which always overwrites it and neither renews tokens nor decides access) and sends to `/login?next=<full path>`; `next` is still validated by `safeNext`.
  - **`next` and the user's area:** for the player, their area is the public pages (everything except `/manage`, `/admin`, `/login` and `/register`); an owner only accepts paths under `/manage` and an admin only under `/admin`.
  - **Logout:** always answers 204 and deletes the two cookies, with or without a valid session. An already used refresh token that shows up on logout ends all the account's sessions, as in refresh.
  - **Refreshes:** always come from the browser (the `refresh_token` cookie only goes to `/api/auth`, so the Next.js server never renews the session); the refresh limit therefore counts the user's real IP.
- Decided (confirmed):
  - The email is compared case-insensitively and without the surrounding spaces; the password is used as typed.
### US04 · Admin account created by seed · M · 2 points
 
As the platform owner, I want the admin account to be created at installation, so that there is no public way for anyone to become an admin.
 
- The admin account is created at startup from environment variables (email and password), if it does not exist yet.
- There is no endpoint or page that creates admin accounts or promotes other accounts to admin.
- Without the variables set, the application starts without an admin and logs a warning.
- Only one of the variables set, invalid email or password with fewer than 8 characters: the API does not start and shows a clear error, which never includes the email or the password.
- If an admin already exists, another is not created, even with another email, and the existing admin is never changed (changing the password variable afterwards has no effect).
- If the email already belongs to a player or owner account, that account is never promoted to admin; a warning is left in the log, without the email.
- The seed runs in every environment, always after the migrations. In production the migrations are applied on deploy (T06), before the API starts.
- Changing the admin's password is done with the API's `set-admin-password` command (see the README, "Changing the admin password"): it reads the new password from the `ADMIN_NEW_PASSWORD` environment variable, never from the command line, validates it with the US01 rules, updates the admin's password, ends their sessions and never writes the value anywhere. The old procedure (deleting the admin account) no longer exists.
- The criterion "the admin signs in through the normal login and goes to `/admin`" moved to US03.
- Implementation decisions:
  - Variables `AdminSeed__Email` and `AdminSeed__Password` (in docker compose, `ADMIN_SEED_EMAIL` and `ADMIN_SEED_PASSWORD` in `.env`). The password follows the US01 rules.
  - The surrounding spaces of the email are removed before validating it; the rest of the validation is strict (the password is used as is).
  - All accounts' passwords are stored with PBKDF2-HMAC-SHA512 and 210,000 iterations (Identity's v3 format); old hashes with fewer iterations are upgraded at the next login.
### US05 · Recover password · D · 3 points
 
As a user, I want to reset my password by email, so that I can regain access if I forget it.
 
- The recovery request always shows the same message, whether the email exists or not.
- The email carries a single-use link that expires after 1 hour.
- A used or expired link shows an error and allows requesting another.
- The new password follows the US01 rules.
- Status: done on the back end (`POST /api/auth/password-reset/request` and `POST /api/auth/password-reset/confirm`, both emails); the request page (`/forgot-password`) and the link page (`/reset-password`) on the front end.
- Implementation decisions (decided):
  - The request always answers 202 with the same message, whether the account exists or not; only an empty or badly typed email is a 400 on the `email` field (that reveals nothing). It only looks the account up and queues work: there is no email or hash to calculate in the request.
  - Only an **active** player or owner receives the email. Unknown accounts, deactivated owners and players and the admin receive nothing (the admin changes the password with `set-admin-password`, US04).
  - At most **one recovery email per account every 5 minutes**; extra requests get the same response and do nothing. Per-IP rate limiting like the other authentication routes: **5** requests per minute on the request and **10** on the confirmation (`RateLimiting:Auth:PasswordResetRequestPermitLimit` and `…ConfirmPermitLimit`).
  - The queue message (`password_reset`) only stores the account id. The token is created by the worker when it sends the email, with Identity's password-reset token provider (Data Protection) and a validity of **1 hour** (`DataProtectionTokenProviderOptions.TokenLifespan`; that provider uses the system clock, not `TimeProvider`). The token includes the account's security stamp, so it stops being valid when the password changes: each link works once, and an old link stops working when a more recent one is used.
  - The link is `{PublicSite:BaseUrl}/reset-password?email=…&token=…` (the email and the token escaped). `PublicSite:BaseUrl` is required (validated at startup): `http://localhost:3000` in development, the public HTTPS address in production (T06).
  - The confirmation receives `{ email, token, newPassword }`. The new password follows US01 (400 on the `newPassword` field); any bad link (unknown email, wrong, expired or already used token, deactivated or admin account) is the same 400 `invalid_or_expired_reset_link`, with no fields.
  - If it works: 204, password changed by Identity (new security stamp), all the account's refresh tokens revoked, attempt lockout cleared, and a `A tua palavra-passe foi alterada` (Your password was changed) email queued (`password_changed`, no link). **It does not sign in automatically**: the person signs in with the new password.
  - The email, the token, the link or the password never go to the logs (there is a test).
  - The Data Protection keys that protect the tokens stay in the database (table `data_protection_keys`, `PersistKeysToDbContext`, fixed application name `SportsBooking`), so a link sent before a restart, a deploy or a second instance keeps working (there is a test). **Database backups also hold the keys**: they must be treated as secrets.
### US30 · Delete my account and export my data · D · 5 points

As a user, I want to delete my account and download my data, so that I can exercise my GDPR rights without having to write an email. Until it exists, the Privacy Policy (T07) points to the request by email, handled by hand.

- In the account area, a player or owner can download their data in a commonly used format (account and bookings).
- They can request the deletion of the account, with confirmation; the personal data stops being associated with the bookings, which continue to exist for the clubs without identifying the person.
- What happens to owner accounts with clubs and to future bookings is decided before starting (today bookings are never deleted and a club cannot be left without an owner).
- Status: done (it came out of T07). Page `/account` (`A minha conta` (My account)), section `Privacidade e dados` (Privacy and data); `GET /api/me/data-export` and `POST /api/me/delete-account`; migration `AddAccountDeletion` (`users.deleted_at`, optional, no new table, so `docs/deployment/supabase-roles.sql` does not change). Decision and alternatives in [ADR 0011](architecture/adr/0011-account-deletion-by-anonymization.md).
- **Provisional (US30 decisions, taken by the project owner):**
  - Export: a JSON file with only the person's own data: profile, bookings (club, pitch, dates, price, state) and version and date of the acceptance of the Terms. No hashes, tokens, internal ids or other people's data. Per-IP limit.
  - Deletion: asks for the password again; in a single transaction it cancels future bookings (freeing the times), anonymizes the account (fixed name `Conta apagada` (Deleted account), email `deleted-<id>@deleted.invalid`, password and security stamp invalidated, phone and acceptance cleared), deletes the refresh tokens and the unsent emails. Past bookings stay, without personal data. No referenced row is deleted.
  - An owner with clubs (422 `owner_has_clubs`, the interface tells them to contact the administration) and the admin (422 `admin_cannot_delete_account`) cannot delete the account.
  - No confirmation email: confirmation is on screen and the cookies are cleared.
- **Provisional (choices made in the implementation, the most conservative where the story did not say):**
  - "Active clubs" = any club: a club has no state of its own, and a club with no active pitches still belongs to the owner.
  - The access token of a deleted account stops working at once (a read by primary key on every authenticated request); it is an exception to the 15-minute rule, which still holds for a deactivated account (US29).
  - The cancellation on deletion ignores the cancellation window and becomes `CancelledByPlayer`; a booking that has already started is not touched.
  - A wrong password is 400 `invalid_password` (a 401 would make the client try to renew the session) and counts towards the attempt lockout, as in login.
  - Limits: 5 exports and 5 deletion attempts per minute per IP (`RateLimiting:Auth:AccountExportPermitLimit`, `AccountDeletePermitLimit`).
  - There was no account page: `/account` was created for all profiles, with an `A minha conta` link for players and owners; the admin sees only the export.
  - A deleted account leaves the admin's totals and owners list, but stays in the registrations charts.
  - An owner's export does not include the clubs (the request was profile, bookings and acceptance).
  - Left unanonymized, because they are not account fields: reasons written by hand by a club or by an admin (`owner_status_changes` is append-only) and the already closed outbox messages (they only have the id). The Policy states the first.
  - `LEGAL_VERSION` became `1.1`, because `legal-info.ts` says to change the version whenever a text changes; whoever already accepted 1.0 does not have to accept again.
  - A fourth end-to-end flow, in addition to T08's three, on request; in E2E the sign-in limit goes up to 100 per minute (`e2e/compose.e2e.yml`), because all the browsers share one address.
## Epic 2 — Club setup
 
### US06 · Create and edit club · M · 3 points
 
As an owner, I want to create and edit my clubs, so that players find them with the right information.
 
- A club has a name, address, locality, phone, contact email and cancellation window in hours (24 by default).
- An owner can have several clubs; each club belongs to a single owner.
- The owner only sees and edits their own clubs; a request for another owner's club returns 404.
- Missing required fields are rejected with a per-field message.
- Changing the cancellation window applies to cancellations made from that moment on.
- Decided: the cancellation window is read from the club at the moment of cancelling and is never copied into the bookings. US19 (and US12) follow this rule.
- Decided (URLs): when the club is created a unique `slug` is generated from the name (for example `clube-desportivo-do-porto`); the `slug` does not change if the name changes, so the public addresses keep working. On a collision a numeric suffix is added (`-2`, `-3`, …). The owner area uses the club's public GUID: `/manage/clubs/{id}`.
- The club stores its time zone (`Europe/Lisbon`). When the clubs table exists, T04's real storage test is repeated with it: instants in UTC and the club's time zone stored.
- Status: done. Back end (`/api/manage/clubs`: create, list, view and edit, with the Owner profile and T02) and front end: `/manage` lists the owner's clubs (by name, 20 per page) and, with no clubs, shows the empty state with `Criar o primeiro clube`; `/manage/clubs/new` creates and `/manage/clubs/{id}` edits (form with one message per field, API errors on the right fields). A club that does not exist and another owner's club show the same `Clube não encontrado` (Club not found) page; an id that is not a GUID does not even reach the API.
- Decided (confirmed):
  - The five text fields are required: name (up to 100 characters), address (up to 200), locality (up to 100), phone and contact email (the same rules as US01: 9 to 15 digits with optional `+`; a simple address with a dot in the domain). Surrounding spaces are removed, and the phone's too.
  - Cancellation window: a whole number of hours, from 1 to 168 (one week). On creation it is optional (24 by default); on editing (`PUT`, which replaces everything) it is required. The database also limits it (check constraint).
  - `slug`: ASCII lowercase letters and digits, no accents, and everything else becomes a hyphen; for example "Clube de Ténis da Foz" gives `clube-de-tenis-da-foz` and "Padel & Co." gives `padel-co` (the `&` is not translated). A name with nothing usable gives `clube`. The base is cut at 80 characters, at a word end. On a collision `-2`, `-3`, … is added (the number after the largest in use). The unique index decides simultaneous creations: the one that loses tries again with the next suffix.
  - Owner's list: 20 per page by default, at most 100, ordered by name ignoring case and then by id.
  - A club never changes owner, and deleting the account of an owner with clubs fails instead of deleting the clubs.
  - The time zone is not edited and does not appear in the form; the response shows it (`timeZone`).
### US07 · Manage pitches · M · 5 points
 
As an owner, I want to add, edit and deactivate pitches, so that I control what is available for booking.
 
- A pitch has a name, sport (from a fixed list: futsal, 7-a-side football, padel, tennis), price per slot in euros and status (active or inactive).
- Changing the price only affects new bookings; existing ones keep the price from the time of booking.
- An inactive pitch does not appear in the search or accept bookings.
- A pitch with future confirmed bookings cannot be deactivated; the message says how many there are.
- Pitches are not deleted, only deactivated, to keep the history.
- Status: done. Back end (`/api/manage/clubs/{clubId}/pitches`: create, list, view, edit, deactivate and reactivate, with the Owner profile and T02) and front end: the `/manage/clubs/{id}` page shows, below the club's data, the list of pitches (active and inactive, by name, 20 per page) with `Adicionar campo` (Add pitch), `Editar` (Edit), `Desativar` (Deactivate) and `Reativar` (Reactivate). The sport is a list with the labels `Futsal`, `Futebol 7` (7-a-side football), `Padel` and `Ténis` (Tennis); the price is typed and shown the Portuguese way (`12,50 €`; it also accepts `12.50`) and goes to the API as a number. Inactive pitches appear marked `Inativo` (Inactive). A repeated name appears as an error on the name field. A pitch requested through a club that is not its own gives 404 `pitch_not_found`; another owner's club gives 404 `club_not_found`, as in US06. Done with T03/US18: deactivating a pitch with confirmed bookings that have not ended yet (future or in progress) is refused with 422 `pitch_has_future_bookings`, and the message and the `count` field say how many there are; the count is made under the pitch row's lock, so no booking can enter between the count and the deactivation.
- Decided (confirmed):
  - Stable sport codes in the API, stored as text: `futsal`, `football7`, `padel`, `tennis` (the Portuguese names are only for the interface). A new pitch starts active.
  - Name: required, up to 100 characters (surrounding spaces removed), unique in each club ignoring case (also against inactive pitches); the same name can exist in another club. A repeated name is a validation error on the `name` field (400), not a 409.
  - Price per slot: greater than €0 and up to €500, with at most two decimal places (`numeric(10,2)`, with a check constraint).
  - `PUT` replaces name, sport and price; the status only changes with `POST …/deactivate` and `POST …/reactivate`, which are idempotent (repeating returns 200).
  - Owner's list: active and inactive pitches, 20 per page by default (at most 100), ordered by name ignoring case and then by id.
### US08 · Schedule and slot duration · M · 5 points
 
As an owner, I want to define the opening hours and slot duration of each pitch, so that the free slots are generated automatically.
 
- For each weekday, the owner sets the opening and closing time, or marks the day as closed.
- The slot duration is 30, 60 or 90 minutes, per pitch.
- Slots are generated from opening, in blocks of the defined duration; a block that goes past the closing time is not generated.
- Changing the schedule does not alter bookings already made; a booking outside the new schedule stays valid.
- Times are entered and shown in Lisbon time (T04).
- The latest closing time is 00:00 (midnight of the same day). **Known MVP limitation:** schedules that go past midnight (for example, 22:00–02:00) are rejected.
- When the schedules table exists, T04's real storage test is repeated with it: schedules stored as local time (`time`) with the club's time zone, and the slots generated in UTC.
- Status: done. Front end: on each pitch in `/manage/clubs/{id}` the `Horário` (Schedule) button opens, below the pitch, the week editor (seven rows, Monday first, each with `Fechado` (Closed) or opening and closing time, plus the slot duration of 30, 60 or 90 minutes; it saves the whole week and the API errors appear on the right row and field) and the slot preview (today's date up to 30 days ahead, in Lisbon time, with `(verão)` ("(summer)") and `(inverno)` ("(winter)") in October's repeated hour). Back end: `GET` and `PUT /api/manage/clubs/{clubId}/pitches/{pitchId}/schedule` (duration and the seven days; `PUT` replaces the whole week) and `GET …/slots?date=YYYY-MM-DD` (read-only preview of the slots generated on that local date, with start and end in UTC, by T04's `SlotGenerator`). Everything with the Owner profile and T02: another owner's club gives 404 `club_not_found` and a pitch requested through another club gives 404 `pitch_not_found`, before any validation. The schedules live in the `pitch_opening_hours` table (one row per open day, `time` without a time zone); T04's storage test was repeated with it. Changing the schedule or the duration does not touch bookings (US17 already says a slot is not free if anything overlaps).
- Decided (confirmed):
  - Times go in half-hour steps (09:00, 09:30, …), without seconds; the API and the database reject anything else (the database only checks the order: closing after opening, or 00:00).
  - A new pitch is closed every day and has 60-minute slots until the owner sets the schedule. Pitches that existed before this migration also get 60 minutes.
  - Format of each day: `{ "closed": true }` or `{ "closed": false, "opens": "09:00", "closes": "22:00" }` (local time, `HH:mm`). All seven days are required in the `PUT`, and a closed day with times is rejected (instead of ignoring them). Errors come per field with the JSON path (`monday.closes`).
  - The preview shows the slots the schedule generates and marks as blocked (`blocked`) those that overlap a block (US09) and as booked (`booked`) those that overlap a confirmed booking (US18), even if that booking was made on an old grid; it does not yet remove slots that have already started. It also works for an inactive pitch. A date outside today to 30 days (in Lisbon time) or badly typed gives 400 on the `date` field.
  - Interface: the preview shows `Reservado` (Booked) on slots with a confirmed booking and `Bloqueado` (Blocked) on those that overlap a block (both, when that is the case); free slots carry no mark. The note below says that blocks and bookings appear marked.
### US09 · Block slots · M · 3 points
 
As an owner, I want to block periods of a pitch, so that nobody books it during maintenance or private events.
 
- A block has a pitch, start, end and reason (optional).
- Slots inside a block do not appear as free to players.
- A block that overlaps confirmed bookings is rejected, and the message lists those bookings so the owner can decide (cancel through US12).
- The owner can remove a future block, and the slots become free again.
- Blocks appear in the calendar (US11).
- Status: done. Front end: the `Bloqueios` (Blocks) button on each pitch in `/manage/clubs/{id}` opens the list of current and future blocks (Lisbon time, reason if any, `Em curso` (In progress) on those that have already started), the form to create one (start and end date and time, which can be on different days, and an optional reason) and the removal, with confirmation, of those that have not started; the API errors (overlap, start in the past, block already started) appear in the right place, and the slot preview shows `Bloqueado` on the affected slots. Back end: `POST`, `GET` and `DELETE /api/manage/clubs/{clubId}/pitches/{pitchId}/blocks[/{blockId}]`, with the Owner profile and T02 (another owner's club: 404 `club_not_found`; pitch of another club: 404 `pitch_not_found`; block of another pitch: 404 `block_not_found`). Creating runs in a transaction that starts by locking the pitch row (`SELECT … FOR UPDATE`, `IPitchLock`) and only then checks the overlap with other blocks and, since T03/US18, with confirmed bookings: a block over bookings gives 409 `block_overlaps_bookings`, with those bookings' times in the message and the list (`bookings`: id, start and end in UTC) in the response. The owner's slot preview (US08) marks as `blocked` the slots that overlap any block.
- Decided (confirmed):
  - The start and end arrive as Lisbon local date and time (`2026-10-25T09:00`), in half-hour steps, and are converted with `ClubTime` (T04's clock-change rules); the response carries UTC instants. Blocks can go past midnight (they are not opening hours).
  - The start cannot be in the past (starting now is accepted); the end has to be after the start and at most 90 days ahead.
  - Optional reason, up to 200 characters.
  - Two blocks on the same pitch cannot overlap (409 `block_overlap`); blocks that touch (one ends when the other starts) are accepted. The check is made under the pitch row's lock, with no database constraint.
  - The list has the current and future blocks (those that have not ended yet) in order of start, 20 per page.
  - Only a block that has not started yet is removed; one that has started or ended gives 422 `block_already_started` and stays in the history. Removing is deleting the row.
  - A block can be created on an inactive pitch.
### US10 · Peak and off-peak prices · D · 5 points
 
As an owner, I want to define different prices per time band, so that I charge more at the hours of highest demand.
 
- The owner defines bands per pitch (weekdays and time range), each with its own price.
- A slot outside any band uses the pitch's base price.
- Overlapping bands on the same pitch are rejected.
- The price shown to the player and stored in the booking is that of the slot's band.
- Status: done. Back end: bands, a single price per slot (`Pitch.PriceForSlot`), the band's price in bookings, in the public slots and in the owner's preview. Front end: the `Faixas de preço` (Price bands) button on each pitch in `/manage/clubs/{id}` (beside `Horário` and `Bloqueios`, one panel at a time) opens the list of bands (days in pt-PT starting on Monday, times and price), the form to create and edit (days, start, end, price; API errors go to the right field and the overlap appears above the buttons) and removal with confirmation; the owner's preview, the `Nova reserva` (New booking) form, the player's slot list and the booking summary show each slot's price, and the club page shows the base price and a note when the pitch has bands.
- Decided (confirmed):
  - A band has weekdays (one or more), start and end in the club's local time, in half-hour steps (like US08), end `00:00` = midnight, no bands that go past midnight, and a price with the pitch price's limits (> 0, ≤ €500, two decimal places). Public id GUID v7. At most 20 bands per pitch (422 `price_band_limit_reached`).
  - A slot's price (`Pitch.PriceForSlot`) is that of the band that contains the slot's **local start time** on that weekday (start included, end excluded); outside any band, the base price. A slot that crosses the end of a band (e.g. 90 minutes from 21:00 in a band until 22:00) gets the price of the band where it starts. In the repeated hour of 25 October, the two 01:00 slots have the same price (the local time counts).
  - Bands on the same pitch do not overlap if they share a weekday and the times cross; bands that touch (one ends when the other starts) are accepted. Overlap: 409 `price_band_overlap`; the other errors are 400 `validation_failed` with a per-field message (`days`, `start`, `end`, `price`). Creating, changing and deleting run under the pitch lock (`IPitchLock`), the same as bookings'.
  - Bands are configuration, not history: deleting is deleting the row (`DELETE …/price-bands/{bandId}`, 204). Changing or deleting bands never touches existing bookings, which keep the price they were made with.
  - The search (US16) still shows no prices. The club page shows the base price (`Preço base: 18,50 € por horário` (Base price: €18.50 per time)) and a note when the pitch has bands (`hasPriceBands`); each slot's price comes in `price` in the public slots and in the owner's preview.
  - Weekdays travel in the API as English names (`monday` … `sunday`), like the schedule's keys; the interface shows them in pt-PT, starting on Monday.
  - Only the overlap (409 `price_band_overlap`), the limit (422 `price_band_limit_reached`) and "not found" (404 `price_band_not_found`) have a code of their own; the field errors (empty days, end before start, time off the step, price outside the limits) are 400 `validation_failed` with the message in `days`, `start`, `end` or `price`.
  - Club page text: the pitch's price appears as `Preço base: 18,50 € por horário` and, below, `Alguns horários têm outro preço; o preço de cada horário aparece na escolha do horário.` (Some times have another price; each time's price appears when choosing the time.) when the pitch has bands.
## Epic 3 — Club operations
 
### US11 · Booking calendar · M · 8 points
 
As an owner, I want to see a club's bookings in a daily and weekly view, so that I know what is going on at each pitch.
 
- Daily view: one column per pitch, rows per hour, with bookings and blocks.
- Weekly view: one pitch at a time, chosen from a list.
- Each booking shows the player's name (or the customer's, in US13), the time and the status.
- Navigation to the previous and next day or week, and a `Hoje` (Today) button.
- Cancelled bookings do not appear by default.
- Opening a booking shows the details and the cancel action.
- Blocks appear in the calendar (US09): `GET …/pitches/{pitchId}/blocks` gives each pitch's current and future blocks.
- Candidate for splitting: daily view (5) and weekly view (3).
- Status: done. Back end (`GET /api/manage/clubs/{clubId}/calendar` and `GET …/bookings/{bookingId}`) and interface (the daily and weekly view, `/manage/clubs/{id}/calendar`).
- Decided (confirmed):
  - Back end: the calendar range is at most 7 days, both included; dates have to be between 2000 and 2100 (defensive limit); the owner can see any date (past and future, without the players' 30-day window).
  - Back end: a booking that crosses local midnight appears on both days.
  - Back end: a no-show (`no_show`) is not a cancellation and always appears; only cancelled ones (by the player or by the club) are hidden without `includeCancelled=true`.
  - Back end: an inactive pitch only comes in the list if it has visible bookings in the range (the same criterion that decides which bookings are seen).
  - Back end: the player's name is the only personal data the owner sees, and only in their clubs: no email, phone or account id (there is a test that looks for those values in the response).
  - Interface: the grid widens (in whole hours, within the day) to include a booking or a block outside the opening hours, for example from an old schedule.
  - Interface: bookings and blocks that overlap (for example a cancelled booking and the one that replaced it) sit side by side, in lanes.
  - Interface: changing day, view, week or pitch closes the open booking.
  - Interface: a pitch that is not in the club, in the weekly view's address, counts as the first; an inactive pitch that the API returns appears marked `Inativo`.
  - Interface: bookings and blocks are positioned by real elapsed time since the start of the Lisbon day, and not by clock hours: 25 October 2026 (25 hours) has 25 rows, with `01:00 (verão)` and `01:00 (inverno)`, and 29 March 2026 (23 hours) has 23, without 01:00. Times are only shown through `lib/datetime.ts`. An opening time that does not exist on that day moves forward past the gap, as in the API (01:30 → 02:30).
  - Interface: in a week with a clock change the column of that day shows `25 h` (or `23 h`) and a note above says that the bookings of that day are drawn by the time actually elapsed, so after the change they are an hour out of step with the hours scale (that of a normal day); the time written on each booking is always right.
- Decided (confirmed) — interface decisions:
  - The daily calendar is at `/manage/clubs/{id}/calendar`, with a `Calendário de reservas` (Booking calendar) link on the club page. One column per pitch (those the API returns) and one row per hour, from the earliest opening to the latest closing of that day, in whole hours. With no pitch open and no bookings or blocks, it says so instead of showing the grid.
  - Each booking shows the player's name, the time and the status (`Confirmada` (Confirmed), `Cancelada pelo jogador` (Cancelled by the player), `Cancelada pelo clube` (Cancelled by the club), `Falta` (No-show)), with one style per status; cancelled ones are struck through and dashed. A block has a style of its own (dashed, stripes) and shows `Bloqueio` (Block), the time and the reason, and cannot be opened. What is outside the pitch's opening hours is shaded, and a pitch closed on that day says `Fechado` (Closed).
  - Navigation: `Dia anterior` (Previous day), `Hoje` (today in Lisbon, not the browser's), `Dia seguinte` (Next day) and a date field.
  - The view state is in the address: `?date=YYYY-MM-DD` (today is not written, so `Hoje` is the plain address), `&cancelled=1` and `&booking=<id>`. An invalid date counts as today and an id that is not a GUID is ignored.
  - Cancelled bookings are hidden by default, with the option `Mostrar reservas canceladas` (Show cancelled bookings).
  - Weekly view (`Vista semanal` (Weekly view) / `Vista diária` (Daily view)): one pitch at a time, chosen from a `Campo` (Pitch) list (the first by default), in seven columns, Monday to Sunday, with the day names in Portuguese (`Segunda-feira, 04/05/2026` (Monday, 04/05/2026)), and rows per hour, with the same boxes and the same position rule as the daily view (each day by the real time elapsed since its start, in `layoutWeek`, which uses `layoutDay`). The grid is shared by the seven columns, from the earliest opening to the latest closing time of the week. One request to the API per week (Monday to Sunday, 7 days), with all the pitches; changing pitch makes no other request.
  - Weekly navigation: `Semana anterior` (Previous week), `Hoje` (today's week, in Lisbon) and `Semana seguinte` (Next week); the date field leads to the week that contains that date. The view state is in the address: `?view=week&date=YYYY-MM-DD&pitch=<id>&cancelled=1&booking=<id>` (the daily view is the default and is not written; the pitch is only written in the weekly view).
  - Opening a booking (clicking it) shows the details below the grid: player, pitch, date, time, price, status, when it was made and, if cancelled, when and why. `Fechar` (Close) closes. A booking that no longer exists says `Esta reserva já não existe.` (This booking no longer exists.). A club that is not the owner's (or does not exist) shows the same `Clube não encontrado` page as club editing.
- Decided (confirmed) — implementation decisions:
  - `GET /api/manage/clubs/{clubId}/calendar?from=YYYY-MM-DD&to=YYYY-MM-DD&includeCancelled=false` (Owner profile, through T02: another owner's club, or a non-existent one, is 404 `club_not_found`, before looking at the query). `from` and `to` are the club's local dates, both included; `from` cannot be after `to` (400 on `from`/`to`/`includeCancelled`, messages in Portuguese).
  - The response has `from`, `to`, `pitches`, `bookings` and `blocks`. The club's pitches: the pitches the API lists, ordered by name; each with `id`, `name`, `sport`, `status`, `slotDurationMinutes` and `days` (one entry per date in the range, with local `opens` and `closes` "HH:mm", or `null` on a closed day; closing "00:00" is midnight). The instants of bookings and blocks go in UTC; the interface shows them in Lisbon time.
  - A booking belongs to the range if it overlaps it (half-open), and a day is the local day: on 25 October 2026 (25 hours) the range is [2026-10-24T23:00Z, 2026-10-26T00:00Z), and both 01:30s count.
  - Bookings: `id`, `pitchId`, `startsAt`, `endsAt`, `status`, `price` and `bookerName`, in order of start. Cancelled ones (by the player or by the club) only come with `includeCancelled=true`. Blocks: `id`, `pitchId`, `startsAt`, `endsAt`, `reason`, of the pitches listed.
  - Players and admins get 403 on these endpoints, and 401 without a session.
  - `GET /api/manage/clubs/{clubId}/bookings/{bookingId}`: the details (pitch, sport, start, end, price, status, `bookerName`, `createdAt`, `cancelledAt`, `cancellationReason` and `canCancel`, true for a confirmed booking that has not started). A booking of another club, even the same owner's, and an unknown id are the same 404 `booking_not_found`.
  - Done in US13: for a booking that the owner made for a customer without an account, `bookerName` carries the customer's name and `createdByClub` is true (the interface does not yet show the mark). The phone number only comes in the details.
### US12 · Cancel booking as owner · M · 2 points
 
As an owner, I want to cancel a booking, so that I can resolve problems such as a broken pitch.
 
- The owner can cancel any future booking of their clubs, without the cancellation window's limit.
- Giving a reason is required.
- The booking becomes `Cancelada pelo clube` (Cancelled by the club) (it is not deleted) and the slot is free again.
- The player receives an email with the reason (US22).
- Status: done. Back end (`POST /api/manage/clubs/{clubId}/bookings/{bookingId}/cancel`) and the action in the calendar's booking details.
- Decided (confirmed):
  - Body `{ "reason": "…" }`: the reason is required, trimmed, at most 200 characters (400 on `reason`).
  - Order of responses: another owner's club 404 `club_not_found` → booking that is not this club's 404 `booking_not_found` (both before validating the body) → 400 → 422 `booking_not_confirmed` (already cancelled, by the player or by the club, or no-show; checked first) → 422 `booking_already_started` (already started; starting now counts as started).
  - The cancellation made by the player (US19) also stores the instant (`cancelled_at`).
- Decided (confirmed) — interface decisions:
  - In the details, a booking that the API says can be cancelled (`canCancel`: confirmed and not yet started) has the `Cancelar reserva` (Cancel booking) button; the screen never calculates it. It opens a form with `Motivo do cancelamento` (Reason for cancellation) (required, up to 200 characters, with the same message as the API) and `Confirmar cancelamento` (Confirm cancellation) / `Manter reserva` (Keep booking). A confirmed booking that has already started says `Esta reserva já começou, por isso já não pode ser cancelada.` (This booking has already started, so it can no longer be cancelled.) and has no button.
  - After cancelling it says `Reserva cancelada. O horário voltou a ficar livre.` (Booking cancelled. The time is free again.), the details show the status, the time and the reason, and the calendar reloads (the booking leaves the grid, unless cancelled ones are in view). No email is promised: the notice to the player belongs to US22.
  - The API's refusals show the message of the code (422 `booking_not_confirmed`, 422 `booking_already_started`, 404 `booking_not_found`), close the form and reload the booking and the calendar; the message stays even if the reloaded booking no longer offers the form. A 400 on `reason` goes to the reason field.
- Decided (confirmed) — implementation decisions:
  - There is no cancellation window for the club: it can cancel up to the instant the booking starts.
  - It runs under the pitch lock (the same as bookings', blocks' and the player's cancellation) and reads the booking again inside it; so if the owner and the player cancel at the same time, only one succeeds and the other gets 422 `booking_not_confirmed`, never a 500. The rule is in the entity (`Booking.CancelByClub`).
  - The booking becomes `cancelled_by_club` and stores the reason (`cancellation_reason`) and the instant (`cancelled_at`); it is never deleted and the slot is free again. The response is the booking, as in the details.
### US13 · Booking on behalf of a customer · M · 3 points
 
As an owner, I want to create a booking for a customer who phoned or turned up, so that all bookings are in the system.
 
- The owner picks a pitch and a free slot and gives the customer's name and mobile number; the customer does not need an account.
- The same rule against duplicate bookings from US18 and T03 applies.
- The booking appears in the calendar marked as "created by the club".
- No email is sent, because the customer has no account.
- Status: done. Back end (`POST /api/manage/clubs/{clubId}/bookings`) and interface in the owner's calendar (daily and weekly view).
- Decided (confirmed):
  - The customer's mobile number is required (in US01 it is optional).
  - The 30-day window (today to today + 30, in Lisbon) also applies to the owner: a phone booking for more than 30 days from now is 422 `outside_booking_window`.
  - A deactivated owner with a still valid session gets 422 `club_unavailable` when creating a booking.
  - The mobile number the owner typed is only shown in the booking details, never in the calendar.
  - Interface: empty hours are only offered on an active pitch, within the opening hours, at an hour that has not ended yet and up to 30 days ahead; they are buttons outside the keyboard order, and the `Nova reserva` button is the keyboard entry point.
  - Interface: a date outside today..+30 goes back to today in the form.
  - Interface: opening the form closes the open booking's details.
  - Interface: the check that a time has already started uses the instant the page was opened; whoever decides, in the end, is the API.
- Decided (confirmed) — interface decisions:
  - There are two entry points to the `Nova reserva` (New booking) form in the calendar: the `Nova reserva` button (the pitch and day being viewed; in the weekly view, the chosen pitch) and clicking an empty hour of a pitch (that hour, that pitch and that day, with the slot already chosen if it is free). The button is disabled if the club has no active pitches.
  - The form asks for the pitch (active only), date (from today to 30 days ahead, in Lisbon), a free time, and the customer's name and mobile number. The times come from the owner's preview (`GET …/pitches/{pitchId}/slots`): only free ones can be chosen; the booked ones (`Reservado`), the blocked ones (`Bloqueado`) and those that have already started (`Já começou` (Already started), including the one starting now) appear disabled. Changing the pitch or the date clears the chosen time.
  - The validations are the API's, in Portuguese: time (`Escolhe um horário livre.` (Choose a free time.)), name required and up to 100 characters, mobile number required with 9 to 15 digits and optional "+" (spaces are removed before sending, as in the API). The API's 400 errors go to the fields.
  - On a conflict (409 `slot_taken` or `slot_blocked`) or a time that no longer exists (422 `invalid_slot`, `slot_started`, `outside_booking_window`, `pitch_inactive`, 404 `pitch_not_found`) the code's message is shown in the form, the time is cleared, the times and the calendar are reloaded, and the form stays open with the name and mobile number already typed. Any other error goes to a notice.
  - After creating, the calendar goes to the booking's day (and, in the weekly view, to its pitch), shows it with the details open, and says `Reserva criada para <nome>.` (Booking created for <name>.). The form says that the customer has no account and so receives no email.
  - A booking created by the club says `Criada pelo clube` (Created by the club) in both views (in the booking's square, with a blue side bar, and in the label for screen readers) and in the details, where `Jogador` (Player) becomes `Cliente` (Customer) and the `Telemóvel` (Mobile number) the owner typed and the `Origem: Criada pelo clube` (Origin: Created by the club) appear. It is cancelled like any booking (US12), with a reason, and still says where it came from.
- Decided (confirmed) — implementation decisions:
  - A booking is a player's or a customer's (name and mobile number), never both and never neither: the check constraint `ck_bookings_player_or_customer` replaces `ck_bookings_has_player` (migration `AddCustomerBookings`, columns `customer_name` and `customer_phone`). The exclusion constraint `bookings_no_overlap` did not change, so a customer and a player compete for the same slots.
  - `POST /api/manage/clubs/{clubId}/bookings` (Owner profile, through T02: another owner's club is 404 `club_not_found`, before validating the body) with `{ pitchId, startsAt, customerName, customerPhone }`. `startsAt` is the instant of the slot's start with a time zone, as the slot lists give it; the end and the price come from the pitch, never from the request. 201 with the booking (the same response as the details) and `Location: /api/manage/clubs/{clubId}/bookings/{id}`, which `GET …/bookings/{id}` resolves.
  - Name and mobile number follow the US01 rules: the name is required and has at most 100 characters; the mobile number has an optional "+" and 9 to 15 digits and is stored without spaces (400 on `customerName` and `customerPhone`, messages in Portuguese).
  - The slot rules are US18's, through the same code (`BookingSlotCheck`, which US18 also uses) and under the same pitch lock: inactive pitch 422 `pitch_inactive` → date outside today..+30 in Lisbon 422 `outside_booking_window` → not a slot 422 `invalid_slot` → already started 422 `slot_started` → block 409 `slot_blocked`; another confirmed booking that overlaps is 409 `slot_taken` (the exclusion constraint, never a 500). A pitch that is not this club's is 404 `pitch_not_found`. The complete order: club (404) → validation (400) → pitch (404) → active owner → lock → slot rules → insert.
  - The price is the pitch's at the time and is stored; changing the price afterwards does not alter it. No email is sent (the customer has no account), and US22 says so.
  - The calendar (`bookerName` and `createdByClub`) and the details (`bookerName`, `createdByClub` and `customerPhone`) show these bookings with the customer's name and the created-by-the-club mark. `bookerName` is the name of whoever made the booking: the player's, or the customer's when the booking is the club's.
  - The owner cancels them as in US12 (`…/bookings/{id}/cancel`, with a reason) and the slot is free again.
  - They never appear in any player's list (`GET /api/me/bookings` only reads bookings with the requester's `player_id`), a player who requests or cancels one by id gets 404 `booking_not_found`, the public pages only show the slot as unavailable (no name, phone or id), and the admin has no access to the owner's endpoints (403).
### US14 · Occupation and revenue · D · 5 points
 
As an owner, I want to see my clubs' occupation and revenue by period, so that I understand how the business is doing.
 
- Filters by club, pitch and date range.
- Occupation = booked hours divided by open hours in the period, per pitch.
- Revenue = sum of the prices of non-cancelled bookings.
- The numbers match the calendar for the same period (verified by a test).
- Status: done. Back end (`GET /api/manage/clubs/{clubId}/statistics?from=&to=&pitchId=` and `GET /api/manage/statistics?from=&to=`, "per club") and screens (`/manage/statistics` and `/manage/clubs/{id}/statistics`).
- Implementation decisions (decided):
  - **A single calculation** for the owner and the admin (US27), `OccupationService`. Booked hours = real time of the **confirmed bookings and no-shows** that overlap the period, clipped at the period's limits; cancelled ones (by the player or by the club) are left out. Revenue = sum of the stored prices of those same bookings, **in full** (a booking clipped at the period's limit counts its whole price).
  - Open hours = bookable time: for each Lisbon day, the slots that the schedule **in force now** generates (`SlotGenerator`) times the slot duration, in real time (there is no schedule history). A pitch open 00:00–00:00 has 25 slots of 60 minutes on 25/10/2026 and 23 on 29/03/2026; a closed day counts 0; the minutes after the last slot that fits cannot be booked and do not count (a 90-minute pitch open 4 h has 3 h open).
  - An **inactive** pitch has no open hours: it only enters with the bookings it already has in the period, and does not appear if it has none.
  - Occupation = booked / open, 0 when nothing was open; rounded to 4 places (0.8125 = 81.25 %), hours to 2 places. Totals add up minutes and divide at the end, never average the rates.
  - Period: Lisbon dates `YYYY-MM-DD`, both included; without dates, the last 30 days (today included); `from` ≤ `to`; at most **366 days**; dates between 2000 and 2100; per-field 400 errors in pt-PT. The `pitchId` filter has to be a pitch of the club (404 `pitch_not_found`).
  - Bookings are added up in the database (one query grouped by pitch, with `LEAST`/`GREATEST`); only the schedules (few rows per pitch) are read into memory.
  - The "per club" list has all the owner's clubs, also those that had nothing open or booked (with zeros).
### US15 · Mark no-show · D · 2 points
 
As an owner, I want to mark a past booking as a no-show, so that I know which players do not turn up.
 
- Only confirmed bookings whose end time has passed can be marked.
- The status becomes `Falta` (No-show) and the marking can be reversed.
- The owner sees how many no-shows a player has in their clubs.
- Status: done. Back end (`POST /api/manage/clubs/{clubId}/bookings/{bookingId}/no-show` and `…/no-show/undo`) and, in the calendar's booking details, the `Marcar falta` (Mark no-show) and `Anular falta` (Undo no-show) buttons and the player's number of no-shows.
- Implementation decisions (decided):
  - Marking: only a **confirmed** booking whose end time has passed (the end time counts as passed). First 422 `booking_not_confirmed` (cancelled, or already a no-show), then 422 `booking_not_ended`. Undoing: the no-show goes back to `confirmed`; a booking that is not a no-show is 422 `booking_not_no_show`. Another club's owner, or a booking of another club: 404 (T02); player and admin: 403.
  - Everything runs under the pitch's `IPitchLock`, like the cancellations, and reads the booking again inside it. If, when undoing, another confirmed booking has already taken the time (a no-show does not occupy it), the `bookings_no_overlap` constraint refuses and the response is **409 `slot_taken`**, never 500; the request leaves nothing half-done.
  - A customer's booking (US13) can be marked, but it does not count per person: `playerNoShows` is `null` on those bookings.
  - The owner's booking details gain `playerNoShows` (the player's no-shows in **all** of that owner's clubs, the booking included; never those of another owner), `canMarkNoShow` (confirmed and already ended) and `canUndoNoShow` (it is a no-show). The owner still only sees the player's name.
## Epic 4 — Search and booking
 
### US16 · Search clubs · M · 5 points
 
As a player, I want to search clubs by sport, locality and date, so that I find where to play.
 
- Filters: sport, locality and date (today by default).
- Each result shows name, locality, sports and whether there are free slots on the chosen date.
- Clubs without active pitches, or of deactivated owners, do not appear. An inactive pitch does not count for the search: neither its sport appears in the results nor is it found by the sport filter (US07).
- A search with no results shows a clear message.
- Paginated results, 20 per page.
- Opening a result shows the club's page, with contacts and pitches. The public address of the club page is `/clubs/{slug}` (the `slug` is generated in US06).
- The search works without a session.
- Status: done. Back end: `GET /api/clubs`, `GET /api/localities` and `GET /api/clubs/{slug}`, all public and read-only, with no data about players or the owner. Interface: the home page `/` is the search and `/clubs/{slug}` is the club page.
- Decided (confirmed):
  - An unknown sport is 400 on `sport`. The codes are exactly the stable ones (`futsal`, `football7`, `padel`, `tennis`): "Padel" with a capital is also 400.
  - `page` goes from 1 to 10000 (defensive limit); any other value, or a non-numeric one, is 400 on `page`, with the message in Portuguese.
  - The locality filters by the whole value: equality ignoring case, accents and extra spaces (`clubs.locality_key`, with an index, calculated by `ClubRules.LocalityKey`); it is not a search by part of the name ("Port" does not find "Porto").
  - `GET /api/localities` returns a plain list of texts (for a select): the localities of the visible clubs, one per spelling (ignoring case and accents, shown in one of the spellings in use), sorted.
  - Each search result lists the sports of all the club's active pitches, even when filtering by a sport.
  - In the search the date is optional (today in Lisbon by default); in the slots it is required (see US17).
- Decided (confirmed) — implementation decisions:
  - Visible = the owner is not deactivated (US29) and the club has at least one active pitch (US07). Everything public starts from `VisibleClubs`; what is not visible does not exist: an unknown club, one without active pitches or one of a deactivated owner give the same 404 `club_not_found`.
  - `GET /api/clubs?sport=&locality=&date=&page=`: 20 per page (fixed), ordered by name ignoring case and then by id. `date` is `YYYY-MM-DD`, today in Lisbon by default, from today to today + 30; outside that or badly typed is 400 on `date`. Empty filters count as absent.
  - Each result carries only `slug`, `name`, `locality`, `sports` (the sports of the active pitches) and `hasFreeSlots` (some active pitch has a free slot on the date, by the same rule as US17). It carries no address or contacts. Availability is only calculated for the clubs on the page.
  - `GET /api/clubs/{slug}`: name, address, locality, phone, contact email and the active pitches (`id`, `name`, `sport`, current `pricePerSlot`), ordered by name. The `slug` has to match exactly. The club's id does not appear (it belongs to the owner area).
- Decided (confirmed) — interface decisions:
  - The search (`/`) has a sport select (`Todos os desportos` (All sports) and the four sports), a locality select (those from `GET /api/localities`) and a date (today in Lisbon by default, from today to today + 30). It searches as soon as a field changes, with no button; it works without a session. The search stays in the address (`?sport=&locality=&date=&page=`), so it can be shared and the back button works. Changing a filter goes back to the first page; an invalid value in the address is ignored and a date outside the period shows the error without asking the API.
  - Each result shows the name (link to `/clubs/{slug}`, with `?date=` if a date was searched), the locality, the sports and `Há horários livres` (There are free times) or `Sem horários livres` (No free times). Pages of 20 with `Anterior` (Previous), `Seguinte` (Next) and `Página X de Y` (Page X of Y) (only if there is more than one). No results: `Não encontrámos clubes com estes filtros. Experimenta outra localidade, outro desporto ou outra data.` (We found no clubs with these filters. Try another locality, another sport or another date.) A page past the end offers `Voltar à primeira página` (Back to the first page).
  - The club page is a Server Component that reads the club from the internal API, without cookies and without cache: contacts (phone and email as links) and the active pitches with the price per slot in pt-PT format (`18,50 € por slot` (€18.50 per slot)). An unknown or hidden club, or a slug that cannot exist, shows the `Clube não encontrado` page, without saying why.
### US17 · See free slots · M · 5 points
 
As a player, I want to see a pitch's free slots on a date, so that I can choose the time.
 
- Shows the day's slots with start time, end time and price.
- A slot does not appear as free if it has a confirmed booking, is blocked, is outside the opening hours or has already started.
- Only dates from today up to 30 days ahead can be chosen.
- An inactive pitch shows no slots and accepts no bookings (US07).
- Decided: a slot is not free if it overlaps any confirmed booking or block, and not only if it has the same start. Changing a pitch's slot duration (US08) does not alter existing bookings, so an old booking may end up misaligned with the new grid and occupy part of a new slot; that slot does not appear as free.
- Times appear in Lisbon time and are right on clock-change days (T04).
- On the day the clock goes back (last Sunday of October), the hours between 01:00 and 02:00 appear twice and carry the indication `(verão)` ("(summer)") or `(inverno)` ("(winter)"); no slot is hidden.
- Status: done. Back end: `GET /api/clubs/{slug}/pitches/{pitchId}/slots?date=`, public and read-only. Interface: the `Horários` (Times) section of the club page.
- Decided (confirmed):
  - In the slots API the date (`YYYY-MM-DD`) is required and goes from today to today + 30 in Lisbon: missing, badly typed or outside the period is 400 on `date`. In the search (US16) it is optional.
  - What is hidden does not exist, and that is checked before the date: an unknown or hidden club (no active pitches, deactivated owner), and an unknown, inactive pitch, one of another club or with an id that is not a GUID, are 404 (`club_not_found` or `pitch_not_found`) even if the date is invalid.
  - Whoever has an owner or admin session sees the slots but cannot choose them (they are not buttons, there is no book button); see US18.
- Decided (confirmed) — implementation decisions:
  - The response is `date` and `slots`; each slot carries `startsAt` and `endsAt` (UTC instants with a time zone), `price` (the pitch's current price) and a single boolean `available`. It never says why it is not free, nor who booked, nor the id of a booking or a block. A closed day returns an empty list.
  - `available` = does not overlap, even partly, a confirmed booking or a block, is within the opening hours and has not started yet (starting now counts as started, as in US18). It is a single calculation (`PitchDayCalendar` and `DaySlot.IsFreeAt`), shared with the owner's preview and with the search's "has free slots", and a test confirms they agree.
  - On 25 October the API returns the slots as distinct instants (the hour from 01:00 to 02:00 appears twice, once in each time zone); the "(verão)/(inverno)" label belongs to the interface.
- Decided (confirmed) — interface decisions:
  - The `Horários` section of the club page has a pitch select (the first by default; shows name, sport and price) and a date (today in Lisbon by default, from today to today + 30). The day's slots appear in Lisbon time, whatever the browser's time zone, with the price, and the hour that repeats in October carries `(verão)` or `(inverno)`. The pitch and the date stay in the address (`?pitch=&date=`).
  - A slot that is not free appears disabled (`disabled`), without saying why: not "booked", not "blocked", not "closed". A day with no slots says `Sem horários neste dia.` (No times on this day.) A date outside the period shows the error without asking the API.
### US18 · Book slot · M · 3 points
 
As a player, I want to book a free slot, so that I secure the pitch.
 
- Only signed-in players can book; without a session, login is requested and the player returns to the same slot.
- Before confirming, a summary appears with club, pitch, date, time and price.
- The booking becomes `Confirmada` (Confirmed) and stores the price of that moment: the pitch's price per slot (US07) copied into the booking. Changing the pitch's price afterwards never alters existing bookings.
- An inactive pitch accepts no bookings (US07).
- Decided and done (T03/US18): deactivating a pitch with future confirmed bookings is refused and the message says how many there are (US07): `POST …/pitches/{pitchId}/deactivate` returns 422 `pitch_has_future_bookings` with `count` (a business rule, as in the Stack's code table; this note used to say 409).
- Decided and done (T03/US18): a block that overlaps confirmed bookings is rejected (409 `block_overlaps_bookings`) and the message lists those bookings (US09); the check is in `BlockService.CreateAsync`, inside the pitch row's lock, and creating a booking locks the same row before checking the blocks.
- Status: done. Back end with T03 (`POST /api/bookings`, Player profile); interface on the club page, with US16 and US17. The confirmation email belongs to US22; seeing my bookings and cancelling is US19.
- Decided (confirmed):
  - The request carries the pitch and the slot's start as an instant with a time zone (`2026-10-25T00:00:00Z`, as the slot lists give it), so that October's repeated hour is never ambiguous.
  - Codes: 404 `pitch_not_found` (unknown pitch); 422 `club_unavailable` (deactivated owner), `pitch_inactive`, `outside_booking_window` (local date outside today to 30 days in Lisbon, including past days), `invalid_slot` (a start that is not a generated slot: closed day, outside the opening hours, off the grid, extra seconds), `slot_started`; 409 `slot_blocked` (a block overlaps, even partly) and `slot_taken` (`Este horário acabou de ser reservado. Escolhe outro.` (This time was just booked. Choose another.)). The 422s are checked in this order; the 409s only after locking the pitch row.
  - A slot that starts exactly now counts as started (`slot_started`).
  - Deactivating a pitch counts the confirmed bookings that have not ended yet: those in progress also prevent it.
  - The statuses in the API are `confirmed`, `cancelled_by_player`, `cancelled_by_club` and `no_show`; in the database they are stored with the enum's name (`Confirmed`, …), as the constraint requires.
  - The 201 response carries the header `Location: /api/bookings/{id}` (relative, like the others), which `GET /api/bookings/{id}` resolves (US19).
- Decided (confirmed) — implementation decisions:
  - The end always comes from the pitch's schedule and the price is the pitch's current price per slot.
  - The transaction locks the pitch row, reads the pitch again (status, price and schedule as they are at that moment), checks the blocks and inserts; overlap with another booking is left only to the exclusion constraint (23P01 → 409 `slot_taken`), which is the last defence and the rule for US13 too.
  - The 201 response carries the booking (`id`, `pitchId`, `clubId`, start and end in UTC, `price`, `status` = `confirmed`).
  - Prepared for US13 and done in it: `player_id` can be null in the table, and the check constraint `ck_bookings_player_or_customer` requires "a player, or the name and mobile number of a customer", never both and never neither (before it was `ck_bookings_has_player`).
- Decided (confirmed): the booking interface
  - What the player chooses (pitch, date and slot) stays in the address (`?pitch=&date=&slot=<instant with a time zone>`), not in a Zustand store: that is what allows returning to the same slot after login and sharing the link. Choosing a free slot opens, below the list, the `Resumo da reserva` (Booking summary) (club, pitch, date, time and price); `Cancelar` (Cancel) closes it, and so does changing pitch or date.
  - Without a session, the summary offers `Entrar para reservar`, which goes to `/login?next=<this address with pitch, date and slot>`; after login the player returns to the same club, pitch, date and slot, with the summary open. `safeNext` did not need to change: it already accepts a public page with a query for a player, and sends an owner or an admin to their own area, because they cannot book. If the slot has in the meantime stopped being free (or no longer exists), `Este horário já não está disponível. Escolhe outro.` (This time is no longer available. Choose another.) appears and there is no summary.
  - Only a player sees `Confirmar reserva`. Owners and admins see the slots but cannot choose them, and there is no book button, and a note says `Só os jogadores podem fazer reservas. Entra com uma conta de jogador para reservar.` (Only players can make bookings. Sign in with a player account to book.)
  - On 409 (`slot_taken`, `slot_blocked`) or 422 (`slot_started`, `invalid_slot`, `outside_booking_window`, `pitch_inactive`, `club_unavailable`) the message of that code is shown (`Este horário acabou de ser reservado. Escolhe outro.` for `slot_taken`), the summary is closed and the slot list is reloaded. A 401 (session ended) goes back to login, with a return to the slot; with no connection to the server the summary stays so as to try again.
  - For a visitor, `club_not_found` says `Este clube já não está disponível.` (This club is no longer available.) (the general message speaks to an owner).
- Decided (confirmed) — interface decisions:
  - Confirming sends only `pitchId` and `startsAt` (the instant with a time zone, as the list gave it); the button is disabled while booking. On 201, `Reserva confirmada` (Booking confirmed) appears with the same data, the slot list and the search reload and the slot becomes disabled.
  - Whoever chooses `Criar conta` on the login screen also returns to the same slot: the link carries the `next` (`/register?next=…`), the player registration page reads it (and the `Entrar` link on that screen also keeps it) and, after the account is created, `safeNext` decides whether to accept it: a public address is followed, any other (another site, owner or admin area, the login page itself) is ignored and the player goes to the home page. Owner registration does not use `next`.
- If the slot was taken in the meantime, the API returns 409 and the player sees `Este horário acabou de ser reservado. Escolhe outro.`, with the slot list updated.
- Past slots or slots beyond 30 days cannot be booked.
### US19 · See and cancel my bookings · M · 3 points
 
As a player, I want to see my bookings and cancel future ones, so that I manage my games.
 
- The list separates `Próximas` (Upcoming) and `Anteriores` (Past), ordered by date.
- Each booking shows club, pitch, date, time, price and status.
- The player can cancel a future booking up to the club's cancellation window; after that, the button appears disabled with the explanation.
- The window is the one the club has at the moment of cancelling (read from `Club.CancellationWindowHours`), never a copy stored in the booking (decided in US06).
- The API refuses cancellations outside the window, even if the request does not come from the front end.
- The cancelled booking becomes `Cancelada pelo jogador` (Cancelled by the player) and the slot is free again.
- Status: done. Back end and interface (the `/bookings` page, `As minhas reservas` (My bookings)). The cancellation email belongs to US22 (the cancellation email says who cancelled); US19 sends no emails.
- Decided (UX audit, batch 1): an unexpected error when cancelling (500 or network failure) shows a toast, not a message at the top of the list; the cancellation's result (success or API refusal) receives the focus, so it stays in view and is read once by the screen reader. It is not a live notice.
- Decided (confirmed):
  - `scope` is required on the my-bookings list: missing or unknown is 400 on `scope`.
  - "Past" are the bookings that have already ended plus the cancelled ones (by the player or by the club) and those marked as no-show, from the most recent to the oldest; "Upcoming" are the confirmed ones that have not ended yet, from the oldest to the most recent, and a booking in progress counts as upcoming. In both the order is by start and then by id.
  - The cancellation deadline (`cancellationDeadline`) only exists for confirmed bookings; for the others it is `null`.
  - At the exact instant of the deadline it can no longer be cancelled: it is only possible strictly before it.
  - `booking_not_confirmed` is checked before the window, so the response for an already cancelled booking does not depend on the clock.
  - Cancelling answers 200 with the already cancelled booking (`cancelled_by_player`).
  - Bookings of a club that has in the meantime become hidden stay in the player's list.
  - `Próximas` and `Anteriores` are two links in a navigation (with `aria-current`, and each with its own address; before they had the role of tabs, without behaving like them). The list groups the bookings by day, with the day as the title (`Hoje · …` (Today · …), `Amanhã · …` (Tomorrow · …)), and each booking is a compact row (time and club, then pitch, price and series). A cancellation refusal appears on the booking's row; success appears above the list, in the same region, and receives the focus (UX audit, batch 2).
  - The tab and the list's page stay in the address (`?tab=past&page=2`; `Próximas` and page 1 are the default), so a reload or the back button keeps the place.
  - The `Cancelar reserva` button only follows the API's `canCancel`: whoever decides whether it can still be cancelled is the API, never a calculation in the browser.
  - The message of 422 `outside_cancellation_window` does not carry the deadline (the front end only sees the code); the deadline is seen in the list, which reloads right after.
- Decided (confirmed) — interface decisions:
  - The `/bookings` page is for players only: without a session it goes to `/login?next=<the page, with the tab and the page>` and returns to it after signing in; an owner or an admin goes to their area (`/manage`, `/admin`). The API is still the one that decides (401/403).
  - Changing tab goes back to the first page. A page in the address that is not a number from 1 to 99999 counts as the first.
  - Each booking shows the club (link to `/clubs/{slug}`), the pitch and sport, the date (with the weekday and the year) and the time in Lisbon time (`lib/datetime.ts`, with `(verão)`/`(inverno)` in October's repeated hour), the price paid and the status in Portuguese: `Confirmada`, `Cancelada pelo jogador`, `Cancelada pelo clube`, `Falta`.
  - Only the `Próximas` can be cancelled (a confirmed booking that has already ended, in `Anteriores`, has no button). The `Cancelar reserva` button asks for a confirmation on the row itself (`Sim, cancelar reserva` (Yes, cancel booking) / `Manter reserva` (Keep booking); only one at a time). After the deadline the button is disabled and says `Já não é possível cancelar: o prazo terminou a dd/mm/aaaa hh:mm.` (It is no longer possible to cancel: the deadline ended on dd/mm/yyyy hh:mm.); before the deadline it says `Podes cancelar até dd/mm/aaaa hh:mm.` (You can cancel until dd/mm/yyyy hh:mm.).
  - After cancelling, `Reserva cancelada. O horário voltou a ficar livre.` (Booking cancelled. The time is free again.) appears and the lists and the free times of the public pages reload (`["my-bookings"]` and `["catalog"]`). If the request is refused (422 `outside_cancellation_window`, 422 `booking_not_confirmed`, 404 `booking_not_found`) the code's message is shown and only the list reloads; a session that ended leads to login. A new booking (US18) also reloads `As minhas reservas`.
  - The player's header has the `As minhas reservas` link (Player profile only) and a booking's confirmation has `Ver as minhas reservas` (See my bookings).
- Decided (confirmed) — implementation decisions:
  - `GET /api/me/bookings?scope=upcoming|past&page=` (Player profile, only the requester's bookings, 20 per page). `page` from 1 to 10000, and non-numeric is also 400 on `page` (message in Portuguese).
  - Each booking carries `id`, `clubName`, `clubSlug`, `pitchName`, `sport`, `startsAt`, `endsAt`, `price` (what was paid, not the pitch's current price), `status`, `canCancel` and `cancellationDeadline` (UTC instant). `canCancel` is true if the booking is confirmed and now is before the start minus the club's current window.
  - `GET /api/bookings/{id}` reads one's own booking; another player's and an unknown id are the same 404 `booking_not_found`.
  - `POST /api/bookings/{id}/cancel` (Player profile). The booking is never deleted and the slot is free again, because the exclusion constraint only looks at confirmed ones. Order of refusals: 404 `booking_not_found` → 422 `booking_not_confirmed` → 422 `outside_cancellation_window`, with `cancellationDeadline` (UTC instant) and the window in hours in the message.
  - The window is read from the club at the moment of cancelling, under the pitch row's lock, never from the booking (US06). The same lock serializes two simultaneous cancellations of the same booking: one succeeds and the other gets 422 `booking_not_confirmed`, never a 500.
  - Owners and admins get 403 on the three endpoints, and 401 without a session.
  - The `bookings` table gets the index by player and start that the Stack already asked for (`ix_bookings_player_id_starts_at`).
### US20 · Recurring weekly booking · D · 8 points
 
As a player, I want to book the same slot every week for some time, so that I do not have to book by hand.
 
- The player chooses the slot and the number of weeks, up to 4.
- Before confirming, the system shows which of the dates are free.
- The free dates are booked in a single operation; the taken ones are indicated and left out.
- Each occurrence is an independent booking, which can be cancelled on its own.
- Candidate for splitting.
- Status: done. Back end: `POST /api/bookings/recurring/preview` and `POST /api/bookings/recurring`. Front end: in the booking summary (after choosing a free slot) the `Repetir semanalmente` (Repeat weekly) option with 1 to 4 weeks (1 = no repeat; the choice goes in the address, `?weeks=`, so the return after signing in keeps it); with more than one week it shows each date (Lisbon time and price) as free or unavailable with the reason in pt-PT, and `Confirmar N reservas` (Confirm N bookings) books the free ones; the result shows what was booked and what was left out, with the link to `As minhas reservas`, and when no date can be booked it shows the message and the requested dates with the reason. A visitor chooses the weeks and signs in; only the player sees the dates. In `As minhas reservas` each booking of a series shows `Reserva semanal: 2 de 4` (Weekly booking: 2 of 4).
- Decided (confirmed):
  - `weeks` is the number of dates, the first included, from 1 to 4. Date n is the same weekday and the same **local** time n weeks later (`ClubTime`), so a clock change does not shift the game's time; the first date is exactly the requested instant (in October's repeated hour, the summer or winter one chosen).
  - The preview only accepts a pitch the public sees (active, of a visible club); any other is 404 `pitch_not_found`. Each date carries start, end, price (`PriceForSlot` of that date) and `free` or `unavailable` with a code: `taken`, `blocked`, `not_a_slot`, `outside_window`, `closed` (the pitch is closed on that day), `pitch_inactive` and, beyond the requested list, `started` (only the first date can have already started). It never says who booked.
  - Booking runs, date by date and in order, the same rules as the single booking (`BookingSlotCheck` under the pitch lock, price from `PriceForSlot`, insertion through the exclusion constraint), each date in its own transaction: a refused date does not stop the others. The bookings made share a `series_id`; a date lost to the constraint (23P01) is left out as `taken`. An unknown pitch is 404 and a deactivated owner's club is 422 `club_unavailable`, for the whole request, as in the single booking.
  - Response 201 with the bookings made and the dates left out (with the code) when at least one was made; 409 `series_unavailable` (with `skipped`) when none. `Location` points to the first booking made.
  - Emails: a single summary of the series for the player (the bookings made, with date, Lisbon time and price, and the dates left out with the reason), in a final transaction after the dates (if the process crashes between the last booking and that step, the summary is lost, never a booking); no confirmation per booking. The owner email (US23) and the reminder (US21) remain one per booking made, in that booking's transaction.
  - The player's list and a booking's detail carry `series` (id, position and total), counting only the bookings the series made, cancelled ones included, in order of start (`2 de 4` (2 of 4)). Cancelling remains per booking (US19).
### US21 · Reminder before the booking · D · 3 points
 
As a player, I want to receive a reminder before the booking, so that I do not forget.
 
- An email is sent 3 hours before the start.
- Cancelled bookings, or bookings made less than 3 hours before the start, generate no reminder.
- Each booking generates at most one reminder, even if the service restarts.
- Status: done on the back end (outbox and worker, no new interface).
- Implementation decisions (decided):
  - The reminder goes into the queue in the same transaction as the booking (a player's), with the send time `start − 3 h` (an instant: the 25 October clock change does not shift it; a test covers that day's winter 01:00). Only if that moment is still in the future: a booking made **less than 3 hours, or exactly 3 hours,** before the start generates no reminder (the exact case is provisional). Customer bookings generate none.
  - The message only stores the booking id; the content (club, address, pitch, date, Lisbon time, `(verão)`/`(inverno)`) is read at send time, so that the email says what is true at that moment. The recipient is the player's current email.
  - A booking made less than 3 hours before the start gets no **late** reminder: it simply has no reminder.
  - When the time comes, the worker only sends if the booking is **still confirmed** and has not started; otherwise it marks the message as **skipped** (`skipped_at`, with the reason `booking_not_confirmed` or `booking_started`, counting neither as a failure nor as a send) and sends nothing.
  - A maximum of one reminder per booking is guaranteed by a unique index (`ux_outbox_messages_booking_notification`, by type and booking id), so neither a bug nor a restart duplicates it; a sent message is never claimed again.
## Epic 5 — Notifications
 
### US22 · Emails to the player · M · 3 points
 
As a player, I want to receive an email when I book and when one of my bookings is cancelled, so that I have confirmation.
 
- The confirmation email carries club, address, pitch, date, time and price.
- The cancellation email says who cancelled and, if it was the club, the reason.
  - The cancellation made by the player (US19, `POST /api/bookings/{id}/cancel`) only changes the booking's state: the email that notifies it comes here, in the background, without making the cancellation fail.
  - A booking that the owner creates for a customer (US13) generates no confirmation email, and its cancellation by the owner does not either: the customer has no account or email, only a phone number.
  - The cancellation made by the owner (US12, `POST /api/manage/clubs/{clubId}/bookings/{bookingId}/cancel`) stores the reason and the time (`cancellation_reason`, `cancelled_at`) and only changes the booking's state: the email with the reason comes here, in the background, without making the cancellation fail.
- Sending is done in the background; a sending failure does not fail the booking or the cancellation and is recorded in the log.
- In development, emails go to a test service (for example, Mailpit) and never to real addresses.
- Status: done on the back end (outbox, background worker and SMTP). There is no interface: emails go to Mailpit in development (http://localhost:8025).
- Decided (confirmed):
  - Worker defaults (`Outbox` settings): batches of 20, one pass every 10 seconds (right after if the batch came back full), the first retry 1 minute later, doubling up to a maximum of 1 hour (1, 2, 4, 8, 16 minutes…), and giving up after 6 failed attempts: the message gets `failed_at` and a Warning is logged with the id and the type, without the address. A message for an account that no longer exists gives up at once (`recipient_not_found`).
  - Delivery is "at least once": if the process stops after sending and before recording the send, that message goes out again. The batch rows' locks are held while the batch is sent.
  - There are no links to the application in the emails until T06 (the public address is missing).
  - The API does not start without `Email:Host` and `Email:FromAddress`.
- Decided (confirmed) — implementation decisions:
  - Table `outbox_messages` (migration `AddOutboxMessages`): `id`, `type` (`booking_confirmed`, `booking_cancelled_by_player`, `booking_cancelled_by_club`), `payload` (jsonb, only what the email shows: the player's id, the name, the club's address and locality, the time zone, the pitch, the start, the end, the price and, on a club cancellation, the reason), `created_at`, `attempts`, `next_attempt_at`, `sent_at`, `failed_at` and `last_error` (the exception type's name, or `recipient_not_found`; never the exception's message, which may carry the address). The player's address and name are not stored in the table: they are read at send time, so the email always goes to the current address.
  - The message is written in the same `SaveChanges` and the same transaction (the pitch lock's) as the booking or the cancellation: a booking that loses the race (23P01) or any refusal leaves no email at all, and of several simultaneous cancellations only the winner leaves one. Bookings for customers without an account (US13) generate no email, neither on creating nor on cancelling.
  - Sending is done by a `BackgroundService` inside the API, which never touches the requests: an SMTP failure or slowness neither fails nor delays a booking or a cancellation. Each pass creates its scope, takes up to a batch of due messages with `SELECT … FOR UPDATE SKIP LOCKED` (inside a transaction that lasts until the batch is recorded), so two API instances never send the same message, and sends them over SMTP (MailKit). The batch rows' locks are held while the batch is sent.
  - The emails are in Portuguese, with plain text and a simple HTML version with the same content. Confirmation: subject `Reserva confirmada: <clube>, dd/mm/aaaa às hh:mm` (Booking confirmed: <club>, dd/mm/yyyy at hh:mm) and club, address, pitch, date spelled out, time and price (`18,50 €`). Cancellation: `Reserva cancelada: …` (Booking cancelled: …) when it was the player, `O clube cancelou a tua reserva: …` (The club cancelled your booking: …) when it was the club, which also carries `Motivo` (Reason). The times are in the club's time zone, with `(verão)`/`(inverno)` in October's repeated hour, as on the pages.
  - SMTP settings in `Email` (validated at startup, and without `Host` and `FromAddress` the API does not start, except in Production, provisional, see "Points to review in T06": `Host`, `Port`, `Security` = `None`/`StartTls`/`SslOnConnect`, `Username` and `Password` together or neither, `FromAddress`, `FromName`, `TimeoutSeconds`); in development they point to Mailpit (`localhost:1025`, and `mailpit:1025` in docker compose). In production they come from environment variables (T06). Addresses, email bodies and SMTP credentials never go to the log.
### US23 · Owner email about a new booking · D · 1 point
 
As an owner, I want to receive an email when a new booking comes into one of my clubs, so that I keep track without opening the app.
 
- The email carries club, pitch, date, time and the player's name.
- The owner can turn these emails off in the club settings.
- Bookings created by the owner themselves (US13) generate no email.
- Status: done. Back end and, in the club settings, the `Receber um email por cada nova reserva` (Receive an email for each new booking) switch.
- Implementation decisions (decided):
  - Per-club setting `notifyOnNewBooking`, **on by default** (also on clubs that already existed, through the migration). It is edited in `PUT /api/manage/clubs/{id}` (and may come in the `POST`); it is optional in both: if a request does not carry it, it stays as it was, so that a form that does not know it does not turn the emails off.
  - When a player books, and the setting is on, the owner's email goes into the queue in the same transaction (a booking that loses the race leaves no email). The email carries club, pitch, date, Lisbon time and the player's name, and nothing else about them.
  - The recipient is the club owner's current email, read at send time. Bookings the owner makes (US13) send nothing, neither to the owner nor to anyone. The message only stores the booking id.
  - If the owner was **deactivated** (US29) before the email is sent, the message is marked as skipped (`skipped_at`, reason `owner_deactivated`), without sending, like a reminder for a cancelled booking; the email never goes to the logs.
## Epic 6 — Administration
 
All the pages and endpoints of this epic only answer the Admin profile (403 for the others) and show counts, never players' names or emails.
 
### US24 · Global indicators · M · 3 points
 
As an admin, I want to see the platform's totals, so that I know its size.
 
- Shows the number of owners, clubs, active pitches, players and confirmed bookings.
- Deactivated accounts and clubs appear separated from the active ones.
- The values come from aggregate queries in the database, without loading lists into memory.
- Status: done. API (`GET /api/admin/stats`) and the cards on the `/admin` page.
- Implementation decisions (decided):
  - The response has owners and players as `{ active, deactivated }`, clubs as `{ total, visible, ofDeactivatedOwners, withoutActivePitches }` (the last three add up to the total: visible = active owner and at least one active pitch, as on the public pages), `activePitches` and `confirmedBookings`. Admins count neither as owners nor as players.
  - Active pitches are the active pitches of active owners (those of a deactivated owner are not available, so they do not count); confirmed bookings are all the confirmed ones, also past ones, of players and customers (US13).
  - There are seven counts and one `GROUP BY` over the accounts (profile and state): nothing is loaded into memory. The accounts DTO only has profile, registration date and state, by construction without name or email.
### US25 · Bookings over a period · M · 3 points
 
As an admin, I want to see bookings over time in a chart, so that I understand whether the platform is growing.
 
- Chart of bookings per day, week or month, as chosen.
- Configurable date range, the last 30 days by default.
- Periods with no bookings appear with zero, instead of disappearing from the chart.
- Cancelled bookings are left out by default, with an option to include them.
- Status: done. API (`GET /api/admin/bookings-over-time?granularity=&from=&to=&includeCancelled=`) and the chart on the `/admin` page.
- Implementation decisions (decided):
  - A booking counts in the period in which it was **created** (not the one in which it is played), by Lisbon's date: `date_trunc(unit, created_at AT TIME ZONE 'Europe/Lisbon')`. So 00:30 on 25 October 2026 (23:30 UTC on the 24th is already the 25th in Lisbon) falls on the 25th. New index `ix_bookings_created_at`.
  - Weeks from Monday to Sunday (ISO), calendar months. The range is widened to whole periods (the response gives the effective `from`/`to`), so there is never a partial period.
  - By default: by day, the last 30 Lisbon days (today and the previous 29). `from <= to`, dates between 2000 and 2100, format `YYYY-MM-DD`; 400 errors with pt-PT messages.
  - Maximums per request: 366 days, 104 weeks or 60 months.
  - All the periods in the range appear, with 0 where there are no bookings (filled in the service, after the database counts).
  - Cancelled ones (by the player or by the club) are left out; a no-show (`no_show`) counts, because it is not a cancellation. Bookings made by the club for a customer (US13) count, they are platform bookings.
### US26 · Owners list · M · 3 points
 
As an admin, I want to see the list of owners with each one's numbers, so that I know who uses the platform and how much.
 
- Each row shows the owner's name, email, registration date, number of clubs, pitches, bookings and distinct players who booked at their clubs.
- Sortable by any column, with a search by the owner's name or email.
- Paginated.
- Status: done. API (`GET /api/admin/owners?search=&sort=&direction=&page=`) and the table on the `/admin` page.
- Implementation decisions (decided):
  - Columns: name, email, registration date, deactivated, clubs, pitches, bookings and players. Sorted by `name`, `email`, `registeredAt`, `status`, `clubs`, `pitches`, `bookings` or `players`, in `asc`/`desc`; name and email sort ignoring case; the tie-break is always the owner's id, so the order is stable. By default, name ascending.
  - The search looks for a piece of the name or email, ignoring case (and never finds players). 20 per page.
  - Pitches: only the owner's active pitches. Bookings: the non-cancelled ones (a no-show counts), including those made for customers. Players: distinct players (`COUNT(DISTINCT player_id)`) with non-cancelled bookings at the owner's clubs; customer bookings have no player and do not count.
  - Everything is calculated by the database, only for the owners on the page. Deactivated owners appear, with the deactivated mark.
### US27 · Clubs with the highest occupation · D · 3 points
 
As an admin, I want to see the clubs with the most occupation in a period, so that I know which ones work best.
 
- Top 10 by occupation rate in the chosen period, with the same formula as US14.
- Shows the club's name, owner, locality and rate.
- Status: done. API (`GET /api/admin/clubs/top-occupation?from=&to=`) and the `Clubes com maior ocupação` (Clubs with the highest occupation) table on the `/admin` page, with the period shared with the charts.
- Implementation decisions (decided):
  - Exactly US14's formula and period (`OccupationService`): a club has the same rate here and in the owner's statistics (there is a test). Clubs with no open hours in the period are left out.
  - Order: the **exact** rate (booked ÷ open, before rounding), then the booked hours, then the club's id. Clubs of a **deactivated** owner are left out (they are not available, US29), just as a club's inactive pitches give no open hours.
  - Each row has the id, the slug, the club's name, the owner's name, the locality, the rate and the booked and open hours; nothing about players.
### US28 · New registrations over time · D · 2 points
 
As an admin, I want to see new owners and players over time, so that I follow adoption.
 
- Chart of registrations per week or month, separated by profile.
- Uses the same range filter as US25.
- Status: done. API (`GET /api/admin/registrations?granularity=week|month&from=&to=`) and the registrations chart on the `/admin` page, with the period shared with the bookings chart.
- Implementation decisions (decided):
  - Counts by Lisbon's date of `users.registered_at` (`date_trunc` after `AT TIME ZONE`, weeks from Monday to Sunday), owners and players separately, never the admin; deactivated accounts count (they registered).
  - The range rules are US25's, shared in the code (`ReportingPeriods.Range`): by default the last 30 days, widened to whole periods, with the effective `from`/`to` in the response, at most 104 weeks or 60 months, dates between 2000 and 2100. Only `week` (default) and `month`; `day` is a 400.
  - All the periods appear, with 0 where there were no registrations. New index `ix_users_registered_at`.
### US29 · Deactivate and reactivate owner · D · 5 points
 
As an admin, I want to deactivate an owner, in case of abuse or at their request, so that they stop using the platform.
 
- Giving a reason is required.
- The deactivated owner cannot sign in, and their clubs stop appearing in the search and accepting bookings.
- Deactivation revokes the owner's refresh tokens, and `POST /api/auth/refresh` refuses deactivated accounts, so no new token is issued.
- **Accepted trade-off (stateless JWT, short lifetime):** an already issued access token stays valid until it expires, at most 15 minutes after the deactivation, because the API validates the JWT without querying the database. During that period the owner can still make requests; after that, the session ends.
- Future bookings are not cancelled automatically; the admin sees how many there are before confirming.
- The admin can reactivate the account.
- Each deactivation and reactivation is recorded (who, when and reason), and that record cannot be edited.
- Status: done on the back end (`GET /api/admin/owners/{id}`, `POST …/deactivate`, `POST …/reactivate`, `GET …/history`) and the `/admin/owners/{id}` page (numbers, status, history and the `Desativar dono` (Deactivate owner) / `Reativar dono` (Reactivate owner) button).
- Implementation decisions (decided):
  - `GET /api/admin/owners/{id}` returns the list row (US26) and `futureBookings`: the confirmed bookings of the owner's clubs that have not ended yet (the one in progress counts; customers' too), the number the admin reads before deactivating. An unknown id or a player's or the admin's is always 404 `owner_not_found`, before the body is read.
  - `deactivate` and `reactivate` take `{ reason }`: required, trimmed, at most **500** characters (400 on the `reason` field). They answer 200 with the owner as `GET` gives it. Already deactivated: 422 `owner_already_deactivated`; not deactivated: 422 `owner_not_deactivated`.
  - Deactivating sets `deactivated_at`, revokes all the owner's refresh tokens and writes the record row, all in the same transaction; the decision is an `UPDATE … WHERE deactivated_at IS NULL`, so two admins at the same time give one change and one 422. It cancels no booking. Reactivating clears `deactivated_at` and does not restore sessions: the owner signs in again.
  - Record in `owner_status_changes` (owner, action `Deactivated`/`Reactivated`, reason, the session's admin, when). There is no code that changes or removes it, and a database trigger refuses `UPDATE`, `DELETE` and `TRUNCATE` (SQLSTATE 23001); the keys to the accounts are `RESTRICT`, so an account with a record cannot be deleted.
  - `GET …/history` lists the changes, the most recent first, with action (`deactivated`/`reactivated`), reason, `adminId`, `adminName` and `at`.
  - The effects already existed and have tests: they cannot sign in (generic 401) or renew the session; their clubs leave the search and the page (404 `club_not_found`) and refuse bookings (422 `club_unavailable`); future bookings stay confirmed; reactivating restores everything.
  - A player can still cancel a booking at a deactivated owner's club, with the club's normal cancellation window (there is a test).
  - The deactivated owner does not see the bookings or the clubs until reactivated; the player sees their bookings as always.
## Technical stories
 
They are not visible to the user, but without them the other stories' estimates are wrong. All are MVP, except T07 and T09, added later.
 
### T01 · Structure, Docker and CI · 5 points
 
As a developer, I want the project to run with one command and be tested on every push, so that I develop without manual setup.
 
- Repository with the ASP.NET Core API, the React app in TypeScript and the test projects.
- `docker compose up` starts the API, the app, PostgreSQL and the test email service.
- The EF Core migrations are applied automatically in development.
- GitHub Actions builds and runs the tests (back end and front end) on every push and pull request; a failure blocks the merge.
- OpenAPI documentation (Swagger) available in development.
- The README explains how to run the project locally.
### T02 · Authorization by profile and by owner · 3 points
 
As the platform owner, I want each user to access only what belongs to them, so that no owner sees or changes another's data.
 
- The endpoints are protected by profile (Player, Owner, Admin) with policies in the API.
- A club's resources (club, pitches, blocks, bookings) check that the club belongs to the authenticated owner.
- Requests for another owner's resources return 404, so as not to reveal that they exist.
- Integration tests cover: owner A neither reads nor changes owner B's data; a player does not access `/manage` or `/admin`; an owner does not access `/admin`.
- Status: done for the clubs (US06). The mechanism is `OwnedClubs` (Application): all the owner area's queries start from the signed-in owner's clubs, and another owner's club gives the same 404 `club_not_found` as a club that does not exist. A test discovers all the `/api/manage` endpoints by itself (401 without a session, 403 for player and admin, 404 for another owner's club on the routes with `{clubId}`), so it also covers the endpoints that come next (pitches, blocks, bookings). How to use it: see AGENTS.md.
### T03 · No duplicate bookings · 5 points
 
As a player, I want to be sure that a booked slot is only mine, so that I do not get to the pitch and find it taken.
 
- The database prevents two overlapping confirmed bookings on the same pitch (for example, with a PostgreSQL exclusion constraint), and not only validation in the application.
- The rule also holds for bookings created by the owner (US13).
- The overlap between bookings and blocks is checked inside a transaction.
- Integration test: several simultaneous requests for the same slot result in exactly one 201 and the rest 409.
- The API returns 409 with a clear message, never a 500.
- Status: done (with US18). The exclusion constraint `bookings_no_overlap` is the Stack's (`btree_gist`, `pitch_id WITH =`, `tstzrange(starts_at, ends_at) WITH &&`, `WHERE (status = 'Confirmed')`), created in the `AddBookings` migration; the 23P01 error is 409 `slot_taken`. Bookings and blocks lock the same pitch row (`SELECT … FOR UPDATE`) before checking the overlap between them. Tests: 20 simultaneous requests for the same slot give exactly one 201 and 19 × 409, never 500; a booking and a block at the same time never both win; a cancelled booking leaves the slot free for the constraint.
### T04 · Dates and time zones · 3 points
 
As a player, I want to always see the right times, so that I do not turn up at the wrong time.
 
- All dates are stored in UTC; opening hours are stored as local time, with the club's time zone (Europe/Lisbon).
- Slot generation is right on clock-change days (March and October), with tests for those days.
- The front end always shows Lisbon time, whatever the browser's time zone.
### T05 · Demo data · 2 points
 
As a developer, I want realistic sample data, so that I can demonstrate the app and see the admin dashboard with numbers.
 
- A command creates owners, clubs, pitches, schedules, players and bookings for the last 60 days and the next 14.
- It only runs with an explicit flag, never by mistake in production. In Production it also requires `DemoData__AllowInProduction=true` on the command's process (T06, for the public demo).
- The data generates meaningful numbers in the admin's charts and lists.
- Status: done. API command `seed-demo` (see the README, "Demo data").
- Implementation decisions (decided):
  - The command is `dotnet run --project backend/src/SportsBooking.Api -- seed-demo` (in docker compose, `docker compose run --rm api seed-demo`). It only runs with exactly that argument, creates the data and exits without starting the server (so the email worker never runs). It refuses in the Production environment without `DemoData__AllowInProduction=true` and refuses if there is already any owner or player (an admin account does not prevent it), with a message and exit code 1; everything is in a single transaction.
  - Data: 5 owners and 10 clubs (2 per owner) in Porto, Vila Nova de Gaia, Matosinhos, Maia, Lisboa, Oeiras and Cascais, 43 pitches of the four sports, with realistic prices (tennis €7 to €16, padel €18 to €32, futsal €35 to €45, 7-a-side football €55 to €70), schedules (some close on Monday or at midnight) and slots of 30, 60 and 90 minutes; 5 blocks (two already past and three future, one of them the maintenance of a padel pitch 5 days from now); 60 players registered over the last 90 days (the owners, before that); and about 10,400 bookings from the last 60 days and the next 14: more in the late afternoon and at weekends, growing over time and shrinking going forward, with confirmed bookings, bookings cancelled by the player (within the club's window), cancelled by the club (with a reason) and no-shows in the past, some freed slots that another player booked again, and about 6 % of bookings for customers (US13).
  - It is deterministic: a fixed seed (`20260101`) and everything calculated from today's date in Lisbon (the data's "now" is the start of today). The same day gives the same names, dates, prices and states; only the ids and the password hashes change.
  - Everything goes through the same entities and rules as the application (`Club.Create`, `Pitch.ChangeSchedule`, `Booking.ForPlayer`, `Booking.ForCustomer`, `CancelByPlayer`, `CancelByClub`): only the rule "no bookings in the past" is bypassed, because the bookings are built directly, without the service. Each booking is in a generated slot of its pitch (`Pitch.SlotsOn`), at the pitch's price, outside the blocks, with no overlap (the exclusion constraint accepts everything), created before it starts, after the player registered and less than 30 days before the start.
  - A no-show (`no_show`) was marked by `Booking.MarkNoShow(now)`, which only accepted a confirmed booking that has already ended (the basis of US15, meanwhile done: the owner's service uses `Booking.RecordNoShow(now)` and `UndoNoShow()`; `MarkNoShow` is the same as `RecordNoShow(now) == Marked` and only the seed uses it).
  - The accounts have a registration date (`users.registered_at`, new in this story, filled in by registration and by the seed; accounts that already existed get the migration's date): it is what the admin's owners list and registrations chart (US26, US28) will read.
  - All the addresses are from the reserved domain `example.test` (`dono01`..`dono05`, `jogador01`..`jogador60`, `geral.clube01`..`geral.clube10`) and all the accounts have the same development password, `demo-password-1`, which is in the README. No messages are created in the outbox, so the seed sends no email. The command only writes counts, never accounts or the password.
  - Accounts that already existed before the migration get the migration's date as `registered_at`; all the demo accounts share the same password hash.
  - Mix of outcomes of past bookings: about 80 % completed, 11 % cancelled by the player, 3 % cancelled by the club and 6 % no-shows; about 6 % of bookings are for customers without an account (US13).
### T06 · Continuous deployment · 5 points
 
As a developer, I want every merge to main to be published automatically, so that I always have a version online to show.
 
- The application is reachable at a public URL with HTTPS.
- Every merge to main that passes CI deploys automatically.
- The secrets (database connection, JWT key, SMTP, admin account) come from environment variables, never from the repository.
  - SMTP (US22, Brevo): `Email__Host`, `Email__Port` (2525, confirmed on the first deploy), `Email__Security` (`StartTls`), `Email__Username`, `Email__Password` and `Email__FromAddress` (a sender verified on Brevo); without `Email__Host` and `Email__FromAddress` the API starts in Production, warns and sends nothing (provisional). With more than one API instance, the outbox worker can run in all of them (`SKIP LOCKED`).
- The migrations are applied on deploy in a controlled way.
- The database has two users. The migration bundle runs as the database owner, who creates the `btree_gist` extension (a trusted extension in PostgreSQL 13 or later) and the rest of the schema. The API connects with a separate application user, with only read and write rights on the data and no DDL, so it never changes the schema or creates extensions.
- There is a `/health` endpoint to check whether the API is responding. It only says whether the process is alive and does not touch the database (part A of T06).
- `PublicSite__BaseUrl` is the site's public HTTPS address, used in the emails' links (US05); without it the API does not start.
- The public read endpoints (`/api/clubs`, `/api/localities`, the club page and the slots) have a per-IP request limit, loose enough for normal browsing (searching, opening clubs, changing date and pitch), so that nobody can use them to extract all the data or overload the API. It answers 429 `too_many_requests` with `Retry-After`, as on the authentication routes, and uses the same client IP (see the `ForwardedHeaders` criterion below).
- Status: **done and in production since 2026-11-09 (first real deploy).** The workflows (`deploy.yml`, `prod-data.yml`), the database roles (`docs/deployment/supabase-roles.sql`) and the [runbook](deployment/runbook.md) were used on the first deploy. What the first deploy confirmed: `ForwardLimit=1`, Brevo on port 2525 with `StartTls`, login and a request that changes data through the Vercel rewrite (`Origin`, `Set-Cookie`), nothing cached on the rewrites and Render's fields (Dockerfile, context and health check) as in the runbook; nothing differed from the documentation. The points in "Points to review in T06" stay provisional: client IP (best-effort), SMTP in production, migrations, Vercel CLI, waiting for the API and PostgreSQL version. The request limit of the public read endpoints is done: 120 requests per minute per IP, a single limit shared by the four endpoints (`RateLimiting:Public:Window` / `ReadPermitLimit`, provisional values), with the same per-IP partition and the same trusted-proxy rules as the authentication routes.
- Dependabot is on (`.github/dependabot.yml`: NuGet, npm, GitHub Actions and the back end and front end `Dockerfile`s; Docker was added in `chore/repo-hygiene`). **Done**, before T06: every Monday, one PR grouped per ecosystem for minor and patch updates, at most 5 open PRs (3 on Actions) and a 14-day `cooldown` to respect the rule of versions at least two weeks old (security fixes do not wait). Major updates come one by one and are reviewed by hand.
- Repository hygiene (`chore/repo-hygiene`, after T06): MIT licence (`LICENSE`, also in `frontend/package.json`), `SECURITY.md` (how to report a vulnerability; personal demo, only `main` is supported, no guaranteed deadlines) and CodeQL (`.github/workflows/codeql.yml`: C# and JavaScript/TypeScript, no build, on every PR, on every `push` to `main` and on Mondays; a separate workflow, which `deploy.yml` does not depend on). CodeQL (and `SECURITY.md`'s private vulnerability reporting) only work with the repository public.
- **Closed (`fix/club-not-found`):** the page of a club that does not exist (`/clubs/<unknown>`) answered HTTP 200 instead of 404. Cause: the `(player)` group's `loading.tsx` starts sending the response before `notFound()` runs, and the status is fixed on the first byte (Next.js only adds `noindex`). The same held for a malformed GUID in `/manage/clubs/{id}…` and `/admin/owners/{id}`. Fix: `proxy.ts` answers 404 before the page sends (`lib/not-found-check.ts`): for an impossible slug or GUID, without asking the API, and for a public club the API does not have (one request per slug per minute, only on real loads; if the API fails or is slow, the page goes through). Tests: `src/proxy-not-found.test.ts`, `src/lib/not-found-check.test.ts` and `frontend/scripts/check-not-found.mjs`, which CI runs against the build with a simulated API. **Provisional:** a well-formed GUID that does not exist (or that is another owner's) still answers 200 with the client's "not found" screen, because it is only known with the session; these are pages behind a login. The cost on the happy path is one API request on the first visit to each club, per minute.
- The security headers `Content-Security-Policy`, `X-Content-Type-Options` and `Referrer-Policy` are set on the responses, with tests. **Done**, before T06: the site (Next.js, `frontend/src/lib/security-headers.ts`, applied to all pages) also sends a restrictive `Permissions-Policy` and `frame-ancestors 'none'` and no longer sends `X-Powered-By`; `/reset-password` keeps its `Referrer-Policy: no-referrer` and `Cache-Control: no-store`; the API sends `X-Content-Type-Options: nosniff` on every response and no longer sends `Server`; Swagger stays Development-only. HSTS and `upgrade-insecure-requests` are **defined but off**: they are turned on with `ENABLE_HSTS=true` in the front end build (the `Dockerfile`'s build argument; the headers are written at build time) and **only in T06**, when the site is served over HTTPS. The published site sends HSTS (verified on 2026-11-10: `max-age=63072000; includeSubDomains; preload`, the platform's value, Vercel's). The API, which since [ADR 0009](architecture/adr/0009-deployment-target.md) is public on Render, today sends only `X-Content-Type-Options: nosniff` and sends neither `Server` nor HSTS (the browser only talks to the site's origin, so the HSTS that counts is the site's); whether Render adds any header in front of the API was not verified. **Provisional:** the policy allows `'unsafe-inline'` in scripts and styles, because Next.js with `cacheComponents` sends inline scripts with each page's data (neither a per-request nonce nor a build hash covers them; both were tried in a browser and the application stopped hydrating) and because the calendar grid and Sonner use inline styles. A strict policy requires a nonce and all pages dynamic, that is, giving up `cacheComponents`: an architecture decision still to be made.
- Rate limiting only tells clients apart in production with `ForwardedHeaders__TrustAnyProxy=true` and `ForwardedHeaders__ForwardLimit=1` (confirmed on the first deploy); it is best-effort (provisional, see "Points to review in T06").
- Keeping the demo alive (`keepalive.yml`, after T06): twice a week, a read query against the database through the site (`GET /api/localities`), so that Supabase does not pause the project for inactivity; it uses no secrets and only runs with `DEPLOY_ENABLED=true`. See the [runbook](deployment/runbook.md#10-keep-the-demo-alive) and [ADR 0009](architecture/adr/0009-deployment-target.md).
  - **Provisional (keepalive frequency):** Supabase's documentation says a free project is paused with "low activity over a 7-day period" without giving a number, and that "a few user requests to the database each day" usually suffice; two queries a week may be too few. Increasing the frequency means changing the `cron`.
  - **Provisional (what counts as activity):** Supabase's documentation does not say whether a visit to the dashboard counts as database activity; GitHub's does not define "repository activity" for the 60-day rule that disables scheduled workflows in public repositories.
- Part A (code and configuration) done: proxy to the API set at build time, configurable `Origin`, client IP, Docker image for Render, variables in `docs/deployment/environment-variables.md`, commands in Production.
### T07 · Terms and Privacy Policy · 3 points

As the platform owner, I want to publish the Terms and Conditions and the Privacy Policy and record acceptance at registration, so that whoever creates an account knows what they accept and which data is processed.

- The `/terms` and `/privacy` pages exist, generated on the server, with a common layout, correct titles and regions, and with "Last updated" and the version, which lives in a single module.
- All pages have a link to both in the footer.
- Every statement of fact can be confirmed in the repository, in `docs/deployment` or in the running application; what cannot be confirmed says "to be confirmed".
- The Policy says who is responsible and the contact, which data is collected and what for, the legal basis of each purpose, the processors and what each one receives, how long the data is kept, the cookies, the GDPR rights and how to exercise them, the complaint to the CNPD and who answers for the data of customers without an account entered by owners.
- The Terms say what the service is (personal demo, non-commercial and without guarantees, which may be shut down and takes time to wake up), accounts, booking rules, owners' responsibilities, acceptable use, availability and limits of liability, changes, Portuguese law and the contact.
- There are only strictly necessary cookies and, therefore, no banner; the cookies and storage described are those observed in the browser.
- Both registrations (player and owner) have a required checkbox, `Li e aceito os Termos e Condições e tomei conhecimento da Política de Privacidade` (I have read and accept the Terms and Conditions and have taken note of the Privacy Policy), with links to the two pages and an error message if it is not ticked, usable with keyboard and screen reader.
- The API stores the accepted version and the date (`TimeProvider`) in optional columns of `users`, through the same path in both registrations. A registration without acceptance is refused with a stable code.
- A single migration compatible with the previous API version (optional columns, without touching data). Existing and demo accounts get no value, are not blocked and are never said to have accepted.
- axe finds no violations on `/terms`, `/privacy` and the registration form.
- Status: done. Pages in `frontend/src/app/(legal)`, version and date in `features/legal/legal-info.ts`, footer in the root layout (`SiteFooter`). Registration request with `acceptedTermsVersion`; columns `users.accepted_terms_version` and `users.terms_accepted_at` (migration `AddTermsAcceptance`); without acceptance, 400 `terms_not_accepted`, with the message in `errors.acceptedTermsVersion`. The other fields' errors come first (400 `validation_failed`). `docs/deployment/supabase-roles.sql` does not change: the rights are per table and the new columns inherit them.
- Verified in the running application (docker compose): two cookies (since T09 a third, `NEXT_LOCALE`, only after picking a language; see T09), `access_token` (`/`, httpOnly, Secure, Lax, 15 minutes) and `refresh_token` (`/api/auth`, httpOnly, Secure, Strict, 7 days), only after signing in; nothing in `localStorage` or `sessionStorage`; no request to another origin (the fonts are served by the site itself).
- Site icon: original, hand-drawn for this project (`frontend/src/app/icon.svg`, the project's MIT licence), with `favicon.ico` and `apple-icon.png` generated by `scripts/generate-icons.mjs`; replaces the favicon of unknown origin.
- **Provisional (deploy window):** the new API goes live before the new site, so, during those minutes, registration through the old site gets 400. There is no cheap way to avoid it in a single deploy: the alternative, publishing the site first and only then requiring acceptance in the API, takes two deliveries and leaves accounts without an acceptance record in the interval. Accepted in a demo.
- **Provisional (legal texts):** the legal bases of each purpose and the division of responsibilities over the data of customers without an account were written conservatively and without legal review.
- Locations and transfers (T07 follow-up, 2026-11-10, Policy version 1.2). Stated by the project owner: API on Render in Frankfurt, database on Supabase in West EU (Ireland), Brevo in France and Vercel functions in Washington DC (USA); the site is served by Vercel's global network and the `/api/*` proxy passes through its infrastructure. Safeguards read in each provider's documents on 2026-11-10: Vercel, Data Processing Addendum (updated 2026-03-17, Schedule 3: Standard Contractual Clauses, Decision 2021/914) and Privacy Notice (2026-06-01, section 10: Data Privacy Framework); Render, Data Processing Addendum (section 6.2: Data Privacy Framework and, alternatively, the Clauses) and Privacy Policy; Supabase, Data Processing Addendum (version 1, 2026-08-01, clause 12 and Schedule 2: Clauses, modules 2 and 3) and Privacy Notice (section 6); Brevo, Terms of Service (version of 2025-10-01, data processing annex: Clauses and supplementary measures, Data Privacy Framework for the USA, and a list of processors with hosting in France and Belgium).
- **Provisional (safeguards):** the Policy attributes each safeguard to the provider's own document; certification in the Data Privacy Framework was not confirmed in the official list (dataprivacyframework.gov), which could not be consulted automatically.
- Retention of logs (T07 follow-up, closed on 2026-11-10, Policy version 1.3): the project owner confirmed the free plans (Render's free web service in a Hobby workspace, and Vercel Hobby). Read in the providers' documentation that day: Render, "Logging" page (section "Log limits" → "Retention period": "Hobby — 7 days", retention depends on the workspace plan; the page shows no date; the pricing page says the same) and Vercel, "Runtime logs" page (updated 2026-08-28: "Hobby — 1 hour of logs") and "Limits" page (updated 2026-09-16: "Runtime logs are stored for 1 hour on Hobby"). What the application logs was verified in the code: console logs only, with no sending to another service and no logs table; `Information` level (`Microsoft.AspNetCore` at `Warning`); in production, the text of SQL commands without the values (`EnableSensitiveDataLogging` off; Npgsql does not include error details); 429 with the client partition and the path; unexpected errors with the method, the path and the exception; no call logs an email, password or token. The Policy no longer has "to be confirmed" points.
- **Provisional (accepted version):** the API stores the version the site sends (non-empty, up to 32 characters) and does not compare it with a list, so that the site remains the only place that defines the versions.
### T08 · End-to-end tests · 3 points

As a developer, I want a small, stable set of end-to-end tests that runs in CI against the real stack, so that I have a signal that the site, the API and the database work together.

- `e2e/` exists (Playwright, Chromium only), with three flows and no more: (1) the player signs in with a seeded account, books a free time, sees it in `As minhas reservas` and cancels; (2) two players, in two browser contexts, have the same time on screen: the second gets the 409 `slot_taken` and the interface's message, and only one booking exists; (3) the owner signs in, sees the booking in the calendar and a club that does not exist really answers 404.
- It runs against the repository's `docker compose` with the demo data (`e2e/scripts/stack.mjs`), locally (`npm run e2e` in `e2e/`) and in CI (`.github/workflows/e2e.yml`, separate from `ci.yml` so that `deploy.yml` does not wait for it). Decision and alternatives in [ADR 0010](architecture/adr/0010-playwright-end-to-end-tests.md).
- Stable: locators by role and name, no fixed waits, one worker, one retry only in CI, `trace` and `video` on the first retry and the report uploaded as an artifact on failure.
- Repeatable on the same database: each test uses days from day 20 on and cancels the bookings it makes.
- Status: done. **Provisional:** the 3 points and the "After" classification; E2E is not required for the merge (`main` has no branch protection, it is checked by hand).

### T09 · English language · 8 points

As a visitor who does not read Portuguese, I want the whole site and my emails in English, so that I can find a club, book and manage my bookings without a translator.

- Two languages, `pt-PT` (default) and `en`, with the same URLs (no `/en` prefix): the 404 logic of `proxy.ts`, the E2E tests and `check-not-found.mjs` work unchanged.
- The language of a request: the `NEXT_LOCALE` cookie, written only when the person picks a language, then `Accept-Language` (any `en*` is English, any `pt*` is Portuguese), then pt-PT. The cookie is a functional preference (one year, `Path=/`, `SameSite=Lax`, `Secure` on HTTPS, not httpOnly), listed in the Privacy Policy; there is still no cookie banner.
- A `PT | EN` switcher in the header (every area, also on a phone) and in the footer: a labelled group, two buttons named «PT – Português» and «EN – English», the current one with `aria-current`, operable with the keyboard.
- Every user-visible string (texts, forms, Zod messages, toasts, empty states, `aria-label`, `alt`, titles, metadata, error and not-found pages, legal pages) is in `frontend/messages/pt-PT.json` and `messages/en.json`. Dates, numbers and prices through `Intl` in pt-PT or en-GB; the clubs' times stay in Lisbon and the pages say so. `<html lang>`, titles and metadata follow the language.
- API errors are worded by their `code`; the server's own text is never shown. An unknown code gets the general message.
- English Terms and Privacy Policy as a courtesy translation; the Portuguese text prevails. `LEGAL_VERSION` 1.4 (2026-11-11), because `legal-info.ts` says to change it whenever a text changes; nobody has to accept again.
- Emails in the recipient's language: optional `users.preferred_language` (migration `AddPreferredLanguage`, compatible with the previous API; no new table, so `docs/deployment/supabase-roles.sql` does not change), set at sign-up from the site's language, changed on the account page (`PUT /api/me/preferred-language`), in the data export, cleared by the account deletion. Existing accounts have none and get pt-PT.
- Tests: the catalogs have the same keys, no empty value and the same placeholders and tags; locale resolution (cookie, `Accept-Language`, default); the switcher; the language column, the endpoint, the export, the deletion and every email in both languages (backend); a fifth E2E flow (switch to English, reload, book), with the browser asking for pt-PT in the other four; and a lint rule against text written in `.tsx` files.
- Status: **done.** Decision and alternatives in [ADR 0012](architecture/adr/0012-languages-without-locale-in-the-url.md). Library: next-intl 4.14.9 without i18n routing (it supports Next.js 16 and React 19). 907 messages in each catalog.
- **Provisional (choices the brief left open, the most conservative taken):**
  - The estimate (8 points) and the "After" classification.
  - The brand, `Reservas de Campos`, is not translated: one name for one service, also in the legal texts and the emails.
  - Every page is rendered per request (`instant = false` on the root layout): a prerendered shell would be in one language only. The switcher reloads the page, so what was typed and not sent is lost.
  - Field errors of a 400 show the catalog's words for the field (the rule the API checks), never the API's text; they are less specific than the API's sentence in a few cases (for example a duplicate pitch name says the name must not repeat another pitch's).
  - Codes without their own message (they get the general one, as before): `bad_request`, `conflict`, `internal_error`, `invalid_refresh_token`, `method_not_allowed`, `not_found`, `unauthorized`. `pitch_has_future_bookings` and `block_overlaps_bookings`, which showed the general message, now have their own.
  - The switcher changes the site only; the email language changes only at sign-up and on the account page. The admin is not offered it (the admin gets no emails). An unsupported language in a registration is refused (400), not ignored.
  - English formats are en-GB (day before month, 24-hour clock, `€12.50`); the repeated October hour reads «(summer time)» and «(winter time)».
  - API error messages (ProblemDetails `title`/`detail`, FluentValidation) stay in pt-PT: the site never shows them.
  - `global-error.tsx` has no request language: it starts in pt-PT and switches to the cookie's or the browser's language once mounted.
  - The demo seed has no description or rules fields; its free text is 10 values (5 block reasons and 5 club cancellation reasons) stored in one language like anything an owner types, so it is not translated. Production data is not touched.
  - The fifth E2E flow goes beyond the three of ADR 0010 on purpose (ADR 0012).
  - The screenshots and the demo GIF stay in Portuguese.

## Points summary
 
| Epic | MVP (stories) | MVP (points) | After (stories) | After (points) | Done (stories) | Done (points) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 — Accounts and access | 4 | 12 | 2 | 8 | 6 of 6 | 20 of 20 |
| 2 — Club setup | 4 | 16 | 1 | 5 | 5 of 5 | 21 of 21 |
| 3 — Club operations | 3 | 13 | 2 | 7 | 5 of 5 | 20 of 20 |
| 4 — Search and booking | 4 | 16 | 2 | 11 | 6 of 6 | 27 of 27 |
| 5 — Notifications | 1 | 3 | 1 | 1 | 2 of 2 | 4 of 4 |
| 6 — Administration | 3 | 9 | 3 | 10 | 6 of 6 | 19 of 19 |
| Technical stories | 6 | 23 | 3 | 14 | 9 of 9 | 37 of 37 |
| **Total** | **25** | **92** | **14** | **56** | **39 of 39** | **148 of 148** |

Status: all the stories are done. **T06 (continuous deployment, 5 points)** has been in production since 2026-11-09. **T09 (English language, 8 points, provisional)** was added after the MVP.
 
The two 8-point stories (US11 and US20) are the first to split if they do not fit in a sprint.
