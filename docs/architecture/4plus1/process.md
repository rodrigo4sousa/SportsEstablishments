# Process view

<!-- UML sequence diagrams · 4+1 view: process. See README.md. -->

What runs, in which process and thread of execution, what runs at the same time, and how the parts collaborate in requests that touch shared data: transactions, locks, the outbox and the worker. Everything was taken from the code (the pipeline order comes from `Api/Program.cs`, not from the documents); the ids are in the [inventory](../inventory.md).

**How to read the sequence diagrams.** They are plain UML sequence diagrams, with numbered messages. Each participant shows the name of an inventory element (the id is the alias in PlantUML, see [section 12 of the inventory](../inventory.md#12-names-and-aliases-in-the-diagrams)) and the box around it is the container or process where it runs. The class or method that each participant represents is in [section 5](#5-participants-in-each-diagram). A solid arrow is a call (label in English, technology or SQL in square brackets, in the same text); a dashed arrow is the response. `group` marks a transaction, `alt` the possible outcomes, `loop` a repetition. When the same element appears twice (two browser tabs), the second alias gets a suffix (`WEB_API_CLIENT_B`).

## 1. Processes, threads and what runs at the same time

![Processes and threads of execution](../out/process-runtime.svg)

Source: [process-runtime.puml](process-runtime.puml).

| Process | Element | Threads and concurrency |
| --- | --- | --- |
| Browser | C-BROWSER | One JavaScript thread per tab. The tabs share the httpOnly cookies, so they coordinate the refresh with the Web Locks API (section 2.4). Within a tab, simultaneous 401s share a single refresh (`refreshInFlight`). |
| Next.js | C-WEB | A Node.js process with an event loop: many page requests in parallel (asynchronous). `proxy.ts` (WEB-PROXY) runs before the pages of the guarded areas; Server Components call the API at the internal URL; the rewrites (WEB-REWRITE) forward `/api/*`. The server never renews a session. |
| API | C-API | A .NET 10 process (Kestrel). Each request runs on the thread pool, in its own DI scope, with its own `AppDbContext` and its own connection from the Npgsql pool: requests run in parallel and only wait for each other in the database (row locks, constraints). In development the API can also be reached directly on port 5080 ([Architecture notes](../README.md#known-limitations), 1). |
| Email worker | C-WORKER | A `BackgroundService` (`OutboxDispatcher`) inside the API process, which starts and stops with the host. One loop per instance: one pass at a time, each in its own scope. It runs in parallel with the requests; with several API instances there are several workers, and `SKIP LOCKED` stops two from picking up the same message. `Outbox:Enabled=false` turns it off (tests). |
| Command-line commands | C-CLI | The same executable, in another short-lived process, run by the operator (P-OPS): `seed-demo` or `set-admin-password`. `Program.cs` validates the options, applies the migrations (in Development or with `Database:ApplyMigrationsOnStartup`), runs the admin seed, runs the command and exits **before** `MapControllers` and `RunAsync`: it does not start Kestrel or the worker, so it sends no emails. It can run at the same time as an API serving requests; `seed-demo` refuses if there are already owners or players and does everything in one transaction. |
| PostgreSQL | C-DB | One process per connection. `READ COMMITTED` transactions (the default): each statement sees what was committed when it started, which is why the services **read again** after obtaining a lock. |

### Pipeline of an API request

![Pipeline of an API request](../out/process-pipeline.svg)

Source: [process-pipeline.puml](process-pipeline.puml).

The real order, from `Api/Program.cs`:

| # | Middleware | Element | What it decides |
| --- | --- | --- | --- |
| 0 | `UseRouting` (implicit) | — | `WebApplication` puts it before the middlewares in `Program.cs`: the endpoint is chosen right away, and the following ones read its attributes. |
| 1 | `UseForwardedHeaders` | CC-RATE | Client IP from `X-Forwarded-For`, only when the connection comes from a trusted proxy (`ForwardedHeaders:KnownProxies`/`KnownNetworks`, empty by default). |
| 2 | `UseResponseSecurityHeaders` | CC-HEADERS | Puts `X-Content-Type-Options: nosniff` on every response, also on the errors and refusals of the following middlewares (the header is written when the response starts). Kestrel does not send `Server`. |
| 3 | `UseExceptionHandler` | CC-PROBLEM | Wraps everything that follows: an unhandled exception reaches `GlobalExceptionHandler` (Error log, 500 `internal_error`). |
| 4 | `UseStatusCodePages` | CC-PROBLEM | An error response with no body (401, 403, route 404) becomes a ProblemDetails with `code` and `traceId`. |
| 5 | `UseOriginCheck` | CC-ORIGIN | POST, PUT, PATCH and DELETE without an `Origin` from `Security:AllowedOrigins` → 403 `origin_not_allowed`. |
| 6 | `UseRateLimiter` | CC-RATE | Only on endpoints with `[EnableRateLimiting]`: fixed window per IP (IPv6 per /64) → 429 `too_many_requests` with `Retry-After`. |
| 7 | Swagger UI and `MapOpenApi` | CC-OPENAPI | Development only; before authentication, because the fallback policy would block them. |
| 8 | `UseAuthentication` | CC-COOKIES | JwtBearer reads the `access_token` cookie and validates the JWT without going to the database. |
| 9 | `UseAuthorization` | CC-AUTHZ | Fallback policy (session required unless `[AllowAnonymous]`) → 401; profile policy → 403. The per-owner check (T02) comes later, in the services. |
| 10 | Controllers | API-* | Thin controller → service → `Result<T>`; a `ServiceError` becomes a ProblemDetails with `this.Problem(error)` (CC-PROBLEM). |

Consequences of the order: a POST from another origin gets 403 before spending an attempt of the limit; the limit is applied before authentication, so it also counts requests without a session; and the refusals of these middlewares also come out as ProblemDetails (the 403 and the 429 write it themselves).

## 2. Sequences

Each sequence shows one path; refusals appear in the `alt` branches.

### 2.1 Player books a slot

![Player books a slot](../out/process-book.svg)

Source: [process-book.puml](process-book.puml). Code: `App/Bookings/BookingService.cs`, `BookingSlotCheck.cs`; `Inf/Pitches/PitchLock.cs`; `Inf/Bookings/BookingStore.cs`; `App/Notifications/BookingEmailOutbox.cs`.

- **Before the transaction:** validation (400), the pitch and the club (404 `pitch_not_found`) and the active owner (`IsActiveAsync`, 422 `club_unavailable`).
- **Transaction:** `PitchLock.LockAsync` opens the transaction and locks the pitch's row with `SELECT 1 FROM pitches WHERE id = … FOR UPDATE`. Inside it the pitch is read again (a deactivation, price change or schedule change committed earlier is seen), `BookingSlotCheck.CheckAsync` checks active pitch, window, grid, "started" and blocks, and the price comes from `Pitch.PriceForSlot`. The three outbox messages (`booking_confirmed`, `owner_new_booking` if `clubs.notify_on_new_booking`, `booking_reminder` due at `startsAt − 3 h` if that moment is still in the future) are only attached to the same `AppDbContext`; the `SaveChanges` in `BookingStore.TryAddAsync` saves the booking and the messages together, and the `COMMIT` releases the lock.
- **Failures:** any refusal from the check returns the error and the scope is disposed without a commit (ROLLBACK). Overlap with another confirmed booking is not looked for by the code: it is `bookings_no_overlap` (DB-NOOVERLAP) that refuses, with SQLSTATE 23P01; the store detaches the `Added` entries (the booking and the emails), logs the case at Information without the player, and the service answers 409 `slot_taken`. A lost booking never leaves an email in the outbox. In the browser, a 409 closes the summary and reloads the slots.

### 2.2 Weekly series

![Weekly series](../out/process-series.svg)

Source: [process-series.puml](process-series.puml). Code: `App/Bookings/RecurringBookings.cs` (`RecurringBookingService.CreateAsync`, `BookOneAsync`, `RecurringOccurrences`).

- The dates come from `RecurringOccurrences.Starts`: the first as requested, the others on the same local date + 7·n at the same local time (`ClubTime.ToUtc`), so a series that crosses the clock change keeps Lisbon time.
- **One transaction per date.** `BookOneAsync` does the same as a single booking, with its own lock and its own commit. A refused date (`pitch_inactive`, `outside_window`, `closed`, `not_a_slot`, `started`, `blocked`, or `taken` through 23P01) is left out and the next one is tried; those already made stay. For each booking made, the owner notice and the reminder go in, in its transaction; the individual confirmation does not.
- **Partial success:** with at least one booking, 201 with `created` and `skipped`; with none, 409 `series_unavailable` with `extensions.skipped`.
- **Summary:** after the dates, `EnqueueSeriesSummaryAsync` and a separate `SaveChanges` save a single `booking_series_confirmed`. If the process crashes between the last date and that step, the bookings stay and the summary is lost (provisional decision of US20).

### 2.3 Cancellations

#### By the player

![Player cancels](../out/process-cancel-player.svg)

Source: [process-cancel-player.puml](process-cancel-player.puml). Code: `App/Bookings/BookingCancellationService.cs`; `Dom/Bookings/Booking.cs` (`CancelByPlayer`).

- The booking is only found if it belongs to the session's player (the same 404 `booking_not_found` for another player's). Then, under the pitch lock: the booking is read again and the window is `clubs.cancellation_window_hours` **at that moment**, never stored in the booking. `CancelByPlayer(window, now)` decides: `NotConfirmed` → 422 `booking_not_confirmed`; `OutsideWindow` (`now ≥ startsAt − window`) → 422 `outside_cancellation_window` with `cancellationDeadline`; `Cancelled` → the state changes, the email goes into the outbox and a single `SaveChanges` saves both before the `COMMIT`.
- **Concurrency:** two cancellations of the same booking wait for each other on `FOR UPDATE`; the second reads the already cancelled booking and gets 422, never a 500 or a second email. The slot is free immediately, because the constraint only covers `Confirmed`.

#### By the owner

![Owner cancels](../out/process-cancel-club.svg)

Source: [process-cancel-club.puml](process-cancel-club.puml). Code: `App/Bookings/ClubBookings.cs` (`ClubBookingService.CancelAsync`); `App/Pitches/OwnedPitches.cs`.

- Order: the owner's club (404 `club_not_found`) → booking of that club (404 `booking_not_found`) → reason (400) → lock → re-read → `CancelByClub(reason, now)`: 422 `booking_not_confirmed`, then 422 `booking_already_started` (no window; starting now counts as started). The reason and `cancelled_at` are stored and the email to the player goes into the outbox in the same transaction (nothing for a customer without an account).
- **Concurrency:** the owner's cancellation and the player's take the same lock; whoever arrives first wins and the other gets 422 `booking_not_confirmed`.

### 2.4 Sessions: login, refresh, tabs and logout

#### Login and logout

![Login and logout](../out/process-login.svg)

Source: [process-login.puml](process-login.puml). Code: `Api/Controllers/AuthController.cs`; `App/Auth/LoginService.cs`; `Inf/Identity/AccountStore.cs`; `Inf/Auth/SessionService.cs`; `Api/Auth/AuthCookies.cs`.

- Any login failure (unknown email, wrong password, locked-out or deactivated account) is the same 401 `invalid_credentials`, after the same PBKDF2 work (`FallbackPasswordHash`). Five wrong passwords in a row lock the account for 15 minutes (`LoginLockout`).
- `SessionService.StartAsync` stores the SHA-256 of a new refresh token (7 days) and signs a 15-minute JWT with `sub` and `role`; `AuthCookies` writes `access_token` (`Path=/`, `SameSite=Lax`) and `refresh_token` (`Path=/api/auth`, `SameSite=Strict`), both httpOnly and Secure.
- Logout consumes the refresh token with a conditional update; if the token had already been used, it is the same theft signal as in refresh and all the account's sessions are revoked. It always answers 204 and deletes both cookies; the browser clears the TanStack Query cache.

#### Refresh with rotation and reuse detection

![Refresh on the API](../out/process-refresh.svg)

Source: [process-refresh.puml](process-refresh.puml). Code: `Inf/Auth/SessionService.cs` (`RefreshAsync`).

- The presented token is looked up by its hash. Already revoked → reuse: Warning log and revocation of all the account's tokens. Expired → 401. Deactivated account or no profile → revoke all and 401 (this is where deactivation ends the sessions).
- **Rotation:** in a transaction, `UPDATE refresh_tokens SET revoked_at = now WHERE id = … AND revoked_at IS NULL`. Only one request gets the row; the one that gets 0 rows does a ROLLBACK and is treated as reuse. The new token inherits `expires_at` (absolute lifetime of 7 days from login). Any failure deletes the cookies and answers 401 `invalid_refresh_token`.

#### In the browser: Web Lock between tabs and silent refresh on /login

![Refresh in the browser](../out/process-refresh-client.svg)

Source: [process-refresh-client.puml](process-refresh-client.puml). Code: `Web/lib/api/client.ts` (`apiRequest`, `retryAfterUnauthorized`, `refreshSession`, `trySilentRefresh`, `REFRESH_LOCK`); `Web/features/auth/components/login-screen.tsx`.

- On a 401, the client requests the `sportsbooking-refresh` lock; whoever gets it **repeats the original request first**, because another tab may already have renewed the shared cookies, and only if it gets 401 again does it refresh and repeat once more. Without the lock, two tabs would present the same refresh token and the second would be treated as theft. Without `navigator.locks`, only the sharing within the tab applies.
- The server never renews the session (the refresh cookie does not go to the pages). That is why `/login` (`LoginScreen`) first tries, in the browser, `trySilentRefresh()` with the same lock; with 204 it reads `/api/auth/me` and goes on to `safeNext(next, profile)`, otherwise it shows the form.

### 2.5 Password recovery

#### Request

![Requesting a new password](../out/process-reset-request.svg)

Source: [process-reset-request.puml](process-reset-request.puml). Code: `App/Auth/PasswordReset.cs` (`PasswordResetService.RequestAsync`); `Inf/Auth/PasswordReset.cs` (`PasswordResetStore.QueueResetEmailAsync`, `AccountEmailBuilder`); `Inf/Notifications/OutboxProcessor.cs`.

- **No enumeration:** the response is always the same 202 (`PasswordResetRequested.Always`); only a malformed email gives 400. For a non-existent, deactivated or admin account nothing is done.
- **Per-account limit:** in a transaction, the account's row is locked (`SELECT 1 FROM users WHERE id = … FOR UPDATE`), so two simultaneous requests do not both get through; if there is already a `password_reset` message less than 5 minutes old, a silent ROLLBACK. The message carries **only the account id**.
- **Token at send time:** the worker calls `AccountEmailBuilder.BuildAsync`, which checks eligibility again and generates the token with Identity's provider (`GeneratePasswordResetTokenAsync`, Data Protection keys in `data_protection_keys`). The token and the link only exist in the email; they are never in the outbox or in the logs.

#### Confirmation

![Setting the new password](../out/process-reset-confirm.svg)

Source: [process-reset-confirm.puml](process-reset-confirm.puml). Code: `PasswordResetService.ConfirmAsync`, `PasswordResetStore.ResetAsync`.

- First the new password (400 on the field); then every bad link (unknown email, wrong, expired or already used token, empty email or token) is the same 400 `invalid_or_expired_reset_link`.
- **One transaction:** `UserManager.ResetPasswordAsync` validates the token and changes the hash and the security stamp (the token stops working, and earlier ones too), the account's refresh tokens are revoked, the lockout is cleared and `password_changed` goes into the outbox. 204, with nobody signed in.

### 2.6 Email worker

#### Claim, send, postpone, give up

![Email worker](../out/process-outbox.svg)

Source: [process-outbox.puml](process-outbox.puml). Code: `Inf/Notifications/OutboxDispatcher.cs`, `OutboxProcessor.cs`, `SmtpEmailSender.cs`; `App/Notifications/OutboxMessage.cs` (`OutboxRetry`).

- **Claim:** one transaction per batch; `SELECT … FOR UPDATE SKIP LOCKED` of the due messages (none of the three dates filled in and `next_attempt_at ≤ now`), in order, at most 20. Another instance skips the locked rows. The locks are held while the batch is sent.
- **Send:** the recipient is read **now** (the current address, never stored in the payload); the email is composed in the recipient's language (pt-PT or English, T09) in Lisbon time and sent with one SMTP connection per message. Each message's result is saved right away (`SaveChanges`); the batch's `COMMIT` confirms them all.
- **Postpone and give up:** a send failure counts as an attempt (`RecordFailure`, only the exception type in `last_error`) and postpones the next one by 1, 2, 4, 8, 16 minutes (at most 1 hour); on the 6th failure the message gets `failed_at` and a Warning is logged with the id and the type. With no recipient, or a message that cannot be read, it gives up right away (`GiveUp`) without counting an attempt.
- **Pace:** if the batch came back full, the next pass starts immediately; otherwise it waits 10 seconds on the injected `TimeProvider`. A failure of the whole pass (database down) is logged at Error and tried again on the next one.
- **At least once:** if the process stops after a send and before the `COMMIT`, all the messages in that batch become pending again and those already sent go out again.

#### Skipping what no longer applies

![Owner notices and reminders](../out/process-outbox-skip.svg)

Source: [process-outbox-skip.puml](process-outbox-skip.puml). Code: `OutboxProcessor.BuildBookingNotificationAsync`; `AccountEmailBuilder.IsActiveAsync`.

- `owner_new_booking` and `booking_reminder` only store the booking id; the booking, the club and the accounts are read at send time, so the email says what is true at that moment.
- A reminder for a booking that is no longer `Confirmed` (cancelled, no-show) is closed with `Skip(booking_not_confirmed)`; a reminder whose booking has already started (the worker was stopped) with `Skip(booking_started)`; a notice for an owner deactivated in the meantime with `Skip(owner_deactivated)`. Skipping fills in `skipped_at`, sends nothing and does not count as a failure.

### 2.7 Page of a guarded area, rendered on the server

![Guarded page](../out/process-guard.svg)

Source: [process-guard.puml](process-guard.puml). Code: `Web/proxy.ts`; `Web/app/manage/layout.tsx` (and `admin/layout.tsx`, `(player)/bookings/layout.tsx`); `Web/components/area-guard.tsx`, `player-guard.tsx`; `Web/lib/server/session.ts`; `Web/lib/auth/redirects.ts`.

- `proxy.ts` writes the requested path to `x-pathname` (over whatever the client sent) on `/manage/*`, `/admin/*` and `/bookings`; in the same areas and on `/clubs/{slug}` it answers 404 to an impossible id (and, for a club, to one the API does not have) before the page starts sending.
- The area's layout puts the guard inside `<Suspense>`, because reading cookies is dynamic with `cacheComponents`. `getSessionAccount` returns `null` without calling the API if there are no cookies; otherwise it calls `GET /api/auth/me` at the internal URL with the `Cookie` header and `no-store`. A 401 is `null`; any other error throws, and the area's `error.tsx` appears instead of a wrong redirect.
- `areaDecision` decides: no session → `/login?next=<safeNext(path)>`; another profile → that profile's home page; the right profile → the area.
- **Redirect inside Suspense:** when the guard calls `redirect()`, the page shell has already been sent with 200 (the Suspense fallback is empty). As the Next.js 16 `redirect()` documentation describes for streaming, the redirect then follows in the streamed HTML as a `<meta http-equiv="refresh">` tag, and it is the browser that changes page; it is not a 307. On `/login`, `GuestOnly` sends someone who already has a session to their area and `LoginScreen` tries the silent refresh (2.4).
- The guard is a convenience: whoever decides access is the API (401/403), on every request.

### 2.8 Deactivating an owner

![Deactivating an owner](../out/process-owner-deactivation.svg)

Source: [process-owner-deactivation.puml](process-owner-deactivation.puml). Code: `App/Admin/AdminOwnerStatus.cs` (`AdminOwnerStatusService`); `Inf/Admin/OwnerStatusStore.cs` (`TryChangeAsync`).

- Order: Admin session → the owner exists (404 `owner_not_found`, also for the id of a player or of the admin), before the body → reason (400) → the admin comes from `ICurrentUser`, never from the request.
- **One transaction:** the conditional update `UPDATE users SET deactivated_at = now WHERE id = … AND deactivated_at IS NULL` decides; 0 rows → ROLLBACK and 422 `owner_already_deactivated` (two admins at the same time: one 200 and one 422). With 1 row: the audit row in `owner_status_changes` (append-only, protected by the DB-AUDITTRG trigger) and the revocation of all the owner's refresh tokens, and `COMMIT`. Reactivation is the same sequence with the inverse condition, without revoking.
- No booking is cancelled; the response says how many future ones stay (`futureBookings`). The access token already issued is valid for up to 15 minutes (accepted trade-off).

## 3. Concurrency: what is protected and what is not

### What protects what

| Mechanism | Where | Protects |
| --- | --- | --- |
| Exclusion constraint `bookings_no_overlap` (DB-NOOVERLAP) | `bookings`, only `status = 'Confirmed'` | Two overlapping confirmed bookings on the same pitch, wherever they come from (player, series, owner's customer, undoing a no-show). 23P01 → 409 `slot_taken` or the reason `taken`. It has the last word; the code does not look for overlaps between bookings before inserting. |
| Pitch row lock (CC-LOCK, `SELECT … FOR UPDATE`) | `PitchLock` | Serializes, per pitch, everything that decides whether a period is free or what it costs: creating bookings (all three ways), creating blocks (and the refusal because of confirmed bookings), cancelling (player and owner), marking and undoing no-shows, deactivating the pitch (refusal with future bookings) and price bands. Blocks and bookings are in different tables, and this lock is what stops a block and a booking from both getting through. Whoever writes the `pitches` row itself (editing the pitch or the schedule, reactivating) also waits, because an `UPDATE` locks the row. |
| Re-reading inside the lock | Booking services | With `READ COMMITTED`, the read made after obtaining the lock sees what the previous one committed: two cancellations give one 200 and one 422. |
| Conditional updates | `SessionService` (refresh and logout), `OwnerStatusStore` | A refresh token is used once; a deactivation or reactivation happens once. Whoever gets 0 rows lost the race. |
| Unique indexes | `ix_users_normalized_email`, `ix_pitches_club_id_name_key`, `ix_clubs_slug`, `refresh_tokens.token_hash`, `ux_outbox_messages_booking_notification` (DB-OUTBOXUQ) | One email per account (409 `email_taken`), unique pitch name in the club (400 on the name), unique slug (retry), at most one reminder and one owner notice per booking. |
| Account row lock | `PasswordResetStore.QueueResetEmailAsync` | Two recovery requests at the same time queue at most one email. |
| `FOR UPDATE SKIP LOCKED` | `OutboxProcessor` | Two instances (or passes) never claim the same message. |
| Outbox in the same transaction | `BookingEmailOutbox`, `PasswordResetStore` | An email exists if and only if the change was committed: a lost booking leaves no email; a committed one always leaves one. |
| Append-only trigger | DB-AUDITTRG | The owners' history is not changed or deleted (23001), not even by hand-written SQL. |

**Email delivery: at least once.** A committed message is sent until it succeeds or is given up on, but it may go out twice: sending happens before the batch's `COMMIT`, and if the process stops between the two, that batch's messages become pending again.

### What is not protected (said as it is)

- **A series summary lost.** The summary is saved after the dates, in a separate transaction: if the API crashes between the last booking and that `SaveChanges`, the bookings stay and the player does not receive the summary (each booking's owner notice and reminder stay, because they are in its transaction). Provisional decision of US20.
- **A series is not atomic.** Each date is independent: between two dates someone else can book, and the player gets a partial series. It is the requested behaviour, not a defect; the preview is only indicative.
- **Active owner checked before the lock.** `IsActiveAsync` runs before `LockAsync`, and deactivation does not take the pitch lock: a booking that has already passed that check can be confirmed right after the owner is deactivated. The window is milliseconds, and the booking counts in `futureBookings`.
- **Session after deactivating or changing the password.** The access token already issued stays valid for up to 15 minutes; there is no active-account check on every request (accepted trade-off).
- **Login lockout count.** Wrong attempts in parallel on the same account may not all be counted (Identity's concurrency control); rate limiting covers the case (backlog US03).
- **Per-instance rate limiting.** The built-in limiter keeps the windows in the process's memory: with several API instances, each counts separately. And, without a trusted proxy in front, the API sees the IP of the Next.js container ([Architecture notes](../README.md#known-limitations), 2).
- **Duplicate emails.** It is the price of "at least once" (above).

## 4. Time

- **Store in UTC.** All dates are `timestamptz` and `DateTime` with UTC `Kind`; Npgsql refuses anything else. Opening hours and price bands are local `TimeOnly`, plus the club's time zone (`Europe/Lisbon`).
- **Show in Lisbon.** The API returns UTC instants; the front end formats only through `Web/lib/datetime.ts` (WEB-DATETIME), in `pt-PT` or `en-GB` (the language of the request) and `Europe/Lisbon`, whatever the browser's time zone. The emails are composed in the club's time zone.
- **Clock change (T04).** The only local→UTC conversion is `ClubTime.ToUtc` (CC-CLUBTIME): a time that does not exist (last Sunday of March, 01:30) moves forward by the size of the gap (02:30 WEST); a time that repeats (last Sunday of October) uses the **first** occurrence (WEST). Slots come only from `SlotGenerator`, by real elapsed time from opening: on those days there is one slot fewer or one more, and each lasts exactly its duration. In the repeated October hour the UI marks `(verão)` ("(summer)") and `(inverno)` ("(winter)"). `ClubTime.Today` gives Lisbon's date, which may not be UTC's near midnight; it is what defines the booking window. The reminder (`startsAt − 3 h`) is calculated on the instant, so the clock change does not shift it.
- **Injected clock.** All current time comes from `TimeProvider` (CC-TIME: `TimeProvider.System` at run time, `FakeTimeProvider` and `SettableTimeProvider` in the tests), including the worker's wait (`Task.Delay(…, timeProvider)`).
- **The documented exception.** Identity's password-reset token generator (`DataProtectorTokenProvider`, `AddDefaultTokenProviders`) reads the system clock, not `TimeProvider`: the 1-hour validity counts from the sending of the email, by the machine's clock, and the tests fabricate old tokens in Identity's format instead of changing the clock. The same happens with the access token's validity, which JwtBearer checks against the system clock (that is why tests with a fake clock create the sessions on the real clock).

## 5. Participants in each diagram

What was written under each participant's name, diagram by diagram. The boxes are the container's (ASP.NET Core API = C-API, Next.js = C-WEB, Outbox worker = C-WORKER).

### Processes and threads of execution

Source: [process-runtime.puml](process-runtime.puml).

| Name | Technology | Responsibility |
| --- | --- | --- |
| Operator | — | Runs the command-line commands. |
| Browser | One JavaScript thread per tab | Client Components and TanStack Query; the tabs share the httpOnly cookies and coordinate the refresh with navigator.locks. |
| Next.js | Node.js process (event loop) | Server Components and guards in parallel (asynchronous); proxy.ts; rewrites of /api/\*. |
| ASP.NET Core API | .NET 10 process, Kestrel, thread pool | Requests in parallel; each in its own DI scope, with its own AppDbContext and its own pool connection. |
| Outbox worker | BackgroundService in the API process | One loop per instance: one pass at a time, own scope, waits on TimeProvider. |
| seed-demo, set-admin-password | The same executable, another process | Migrations, admin seed, the command, and exit; does not start Kestrel or the worker. |
| PostgreSQL | PostgreSQL 18.6, one process per connection | READ COMMITTED transactions, row locks, exclusion constraint, unique indexes. |
| SMTP server | — | Mailpit (X-MAILPIT) in development. |

### Player books a slot

Source: [process-book.puml](process-book.puml).

| Participant | Box | Class, method or tables |
| --- | --- | --- |
| Booking | — | ClubBooking, createBooking |
| Single booking | ASP.NET Core API | BookingService.CreateAsync |
| Accounts | ASP.NET Core API | IAccountStore.IsActiveAsync |
| Pitch lock | ASP.NET Core API | IPitchLock, PitchLock |
| Slot check | ASP.NET Core API | BookingSlotCheck.CheckAsync |
| Put in the outbox | ASP.NET Core API | BookingEmailOutbox |
| Booking store | ASP.NET Core API | BookingStore.TryAddAsync |
| PostgreSQL | — | pitches, blocks, bookings, outbox_messages |

### Weekly series

Source: [process-series.puml](process-series.puml).

| Participant | Box | Class, method or tables |
| --- | --- | --- |
| Booking | — | ClubBooking, createRecurring |
| Weekly series | ASP.NET Core API | RecurringBookingService.CreateAsync |
| Accounts | ASP.NET Core API | IAccountStore.IsActiveAsync |
| Pitch lock | ASP.NET Core API | IPitchLock, PitchLock |
| Slot check | ASP.NET Core API | BookingSlotCheck.CheckAsync |
| Put in the outbox | ASP.NET Core API | BookingEmailOutbox |
| Booking store | ASP.NET Core API | BookingStore.TryAddAsync |
| PostgreSQL | — | pitches, blocks, bookings, outbox_messages |

### Player cancels

Source: [process-cancel-player.puml](process-cancel-player.puml).

| Participant | Box | Class, method or tables |
| --- | --- | --- |
| My bookings | — | MyBookings, cancelBooking |
| Cancel | ASP.NET Core API | BookingCancellationService.CancelAsync |
| Pitch lock | ASP.NET Core API | IPitchLock, PitchLock |
| Put in the outbox | ASP.NET Core API | BookingEmailOutbox |
| PostgreSQL | — | bookings, pitches, clubs, outbox_messages |

### Owner cancels with a reason

Source: [process-cancel-club.puml](process-cancel-club.puml).

| Participant | Box | Class, method or tables |
| --- | --- | --- |
| Calendar | — | CancelBookingForm, cancelClubBooking |
| Owner bookings | ASP.NET Core API | ClubBookingService.CancelAsync |
| Per-owner authorization | ASP.NET Core API | OwnedPitches.ClubIsOwnedAsync, OwnedBookings.Of |
| Pitch lock | ASP.NET Core API | IPitchLock, PitchLock |
| Put in the outbox | ASP.NET Core API | BookingEmailOutbox |
| Cancel (player) | ASP.NET Core API | BookingCancellationService.CancelAsync |
| PostgreSQL | — | bookings, pitches, clubs, outbox_messages |

### Login and logout

Source: [process-login.puml](process-login.puml).

| Participant | Box | Class, method or tables |
| --- | --- | --- |
| Accounts | — | LoginForm, LogoutButton, login, logout |
| Accounts and sessions | ASP.NET Core API | AuthController |
| Login | ASP.NET Core API | LoginService.LoginAsync |
| Accounts in Identity | ASP.NET Core API | AccountStore.AuthenticateAsync |
| Sessions and cookies | ASP.NET Core API | SessionService, AuthCookies |
| PostgreSQL | — | users, user_roles, refresh_tokens |

### Refresh with rotation

Source: [process-refresh.puml](process-refresh.puml).

| Participant | Box | Class, method or tables |
| --- | --- | --- |
| API client | — | refreshSession |
| Accounts and sessions | ASP.NET Core API | AuthController.Refresh |
| Sessions and cookies | ASP.NET Core API | SessionService.RefreshAsync, AuthCookies |
| refresh_tokens | — | Table |
| users, user_roles | — | Tables |

### Refresh in the browser

Source: [process-refresh-client.puml](process-refresh-client.puml).

| Participant | Box | Class, method or tables |
| --- | --- | --- |
| tab A | — | apiRequest, retryAfterUnauthorized |
| tab B | — | apiRequest, retryAfterUnauthorized |
| Browser | — | navigator.locks, shared cookies |
| API | — | /api/\* |
| Login page | — | LoginScreen, trySilentRefresh |

### Requesting a new password

Source: [process-reset-request.puml](process-reset-request.puml).

| Participant | Box | Class, method or tables |
| --- | --- | --- |
| Accounts | — | ForgotPasswordForm, requestPasswordReset |
| Accounts and sessions | ASP.NET Core API | AuthController.RequestPasswordReset |
| Recovery | ASP.NET Core API | PasswordResetService, PasswordResetStore, AccountEmailBuilder |
| Process the outbox | ASP.NET Core API | OutboxProcessor (in C-WORKER) |
| Data Protection | ASP.NET Core API | Identity's DataProtectorTokenProvider |
| Send | ASP.NET Core API | SmtpEmailSender |
| PostgreSQL | — | users, user_roles, outbox_messages, data_protection_keys |
| SMTP server | — | — |

### Setting the new password

Source: [process-reset-confirm.puml](process-reset-confirm.puml).

| Participant | Box | Class, method or tables |
| --- | --- | --- |
| Accounts | — | ResetPasswordForm, confirmPasswordReset |
| Accounts and sessions | ASP.NET Core API | AuthController.ConfirmPasswordReset |
| Recovery | ASP.NET Core API | PasswordResetService.ConfirmAsync, PasswordResetStore.ResetAsync |
| Data Protection | ASP.NET Core API | Identity's DataProtectorTokenProvider |
| PostgreSQL | — | users, refresh_tokens, outbox_messages |

### Email worker

Source: [process-outbox.puml](process-outbox.puml).

| Participant | Box | Class, method or tables |
| --- | --- | --- |
| Loop | Outbox worker (in the API process) | OutboxDispatcher.ExecuteAsync |
| Process | Outbox worker (in the API process) | OutboxProcessor.ProcessDueAsync |
| Compose | Outbox worker (in the API process) | BookingEmailComposer, BookingSeriesEmailComposer, BookingNotificationComposer, AccountEmailComposer |
| Send | Outbox worker (in the API process) | IEmailSender, SmtpEmailSender |
| Clock | — | TimeProvider |
| PostgreSQL | — | outbox_messages, users, bookings |
| SMTP server | — | — |

### Owner notices and reminders

Source: [process-outbox-skip.puml](process-outbox-skip.puml).

| Participant | Box | Class, method or tables |
| --- | --- | --- |
| Process | Outbox worker (in the API process) | OutboxProcessor.BuildBookingNotificationAsync |
| AccountEmailBuilder | Outbox worker (in the API process) | IsActiveAsync |
| Compose | Outbox worker (in the API process) | BookingNotificationComposer.Reminder, OwnerNewBooking |
| Send | Outbox worker (in the API process) | SmtpEmailSender |
| PostgreSQL | — | outbox_messages, bookings, pitches, clubs, users |

### Page of a guarded area

Source: [process-guard.puml](process-guard.puml).

| Participant | Box | Class, method or tables |
| --- | --- | --- |
| Browser | — | access_token cookies (Path=/) |
| proxy.ts | Next.js | matcher /manage/\*, /admin/\*, /bookings and /clubs/{slug} (not on prefetches) |
| app/manage/layout.tsx | Next.js | Suspense with fallback null |
| AreaGuard | Next.js | areaDecision, safeNext, loginUrl |
| lib/server/session.ts | Next.js | getSessionAccount |
| JwtBearer | ASP.NET Core API | reads the access_token cookie |
| AuthController.Me | ASP.NET Core API | AccountStore.FindAsync |

### Deactivating an owner

Source: [process-owner-deactivation.puml](process-owner-deactivation.puml).

| Participant | Box | Class, method or tables |
| --- | --- | --- |
| Owner page | — | OwnerStatusForm, changeOwnerStatus |
| Authorization | ASP.NET Core API | Admin policy, ICurrentUser |
| Administration | ASP.NET Core API | AdminOwnerStatusService, AdminOwnersService, OwnerStatusStore.TryChangeAsync |
| users | — | Table |
| owner_status_changes | — | Table, append-only (DB-AUDITTRG) |
| refresh_tokens | — | Table |

## Notes

- **File.** This view replaces the planned `05-runtime.md` (which was empty); the matching entry in the [README](../README.md) was updated.
- **Ids.** No new id; all the participants and steps in the diagrams are in the [inventory](../inventory.md). The browser's second tab is the same element (WEB-API-CLIENT) with a suffixed alias.
- **Deviations found between documents and code** (all fixed in the documents):
  - `docs/stack.md` said that `proxy.ts` acts "only for `/manage` and `/admin`" and that the guards live in the layouts of those two areas; the code also covers `/bookings` (`proxy.ts` matcher and `PlayerGuard`). **Fixed.**
  - `docs/stack.md` spoke of rate limiting "on the `/api/auth/*` routes"; the code limits login, the two registrations, refresh and the two recovery steps (not logout or `/me`) and also the public reads (`ClubsController`, `LocalitiesController`). **Fixed.**
  - No document gave the pipeline order. In the code, the `Origin` check comes **before** rate limiting (and exception handling before both); it is stated in section 1.
  - Logout with an already used refresh token revokes all the account's sessions (`SessionService.EndAsync`), and so does refresh of a deactivated account; `docs/stack.md` only described total revocation on reuse during refresh. **Fixed.**
