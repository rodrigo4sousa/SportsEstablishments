# 4+1 views (Kruchten)

Views of the system, each for one reader and one concern. The names are those of the C4 diagrams in [`../c4/`](../c4/README.md) and of the [inventory](../inventory.md): the same name is the same element. The SVGs are in [`../out/`](../out/); the index of all the diagrams is in the [architecture README](../README.md#index).

## Logical view: [`logical.md`](logical.md)

Diagrams: `logical-*.puml` and, as components (C4 level 3), `../c4/03-components-*.puml`.

**For:** whoever writes code or adds features.
**Documented decision:** Clean Architecture in the back end, in four projects with dependencies pointing inward only (Api → Infrastructure → Application → Domain), with no generic repository: Application uses `IAppDbContext`. Entities have private setters and intention-revealing methods, and expected failures are results, not exceptions. What the database has to guarantee stays in the database: the exclusion constraint only covers `Confirmed` bookings ([ADR 0003](../adr/0003-booking-exclusion-constraint.md)), and slots are calculated, never stored ([ADR 0007](../adr/0007-calculated-slots.md)). An account, a refresh token and an owner's status change live in Infrastructure, and the outbox message in Application ([Known limitations](../README.md#known-limitations), 3).

## Process view: [`process.md`](process.md)

Diagrams: `process-*.puml`.

**For:** whoever writes or debugs requests, transactions, concurrency, sessions and emails.
**Documented decisions:**
- **Concurrency:** booking, blocking and cancelling take the pitch lock (`SELECT … FOR UPDATE`) and read again what they are about to change; overlap with another booking is the exclusion constraint's job, which gives 409 `slot_taken` ([ADR 0003](../adr/0003-booking-exclusion-constraint.md)). Section 3 of the text says what is and is not protected.
- **Sessions:** httpOnly cookies, refresh with rotation, and the server never renews the session ([ADR 0002](../adr/0002-session-in-httponly-cookies.md)).
- **Emails:** saved in the outbox in the requester's transaction and sent by a worker with `SKIP LOCKED`, which runs in the API process ([ADR 0004](../adr/0004-outbox-for-emails.md)).

## Development view: [`development.md`](development.md)

Diagrams: `development-*.puml`.

**For:** whoever is going to change the code, CI or the deploy.
**Documented decision:** one repository with the back end, the front end, the documentation and the workflows. Each layer registers its own services and `Program.cs` only composes. The API types in the front end are generated from OpenAPI, and CI fails if the OpenAPI, the types or the diagram SVGs do not match what the code and the `.puml` files produce. The deploy runs after green CI on `main`, with the migrations applied before the API ([ADR 0009](../adr/0009-deployment-target.md)).

## Physical view: [`physical.md`](physical.md)

Diagram: [`physical.puml`](physical.puml). The C4 deployment is in [`../c4/README.md`](../c4/README.md#deployment).

**For:** whoever deploys, operates or supports.
**Documented decision:** the site on Vercel, the API and the email worker in the same public Render service, the database on Supabase (always through the pooler in session mode) and SMTP on Brevo, all on free plans; the browser only knows Vercel's origin ([ADR 0009](../adr/0009-deployment-target.md)). The text covers what is public, TLS, the client IP, the database roles, the secrets, the migrations and the open risks.

## Scenarios (+1): [`scenarios.md`](scenarios.md)

Diagram: [`scenarios-use-cases.puml`](scenarios-use-cases.puml). There is no sequence per scenario: each one links to the `process-*.puml` files.

**For:** everyone (product, whoever writes the code, whoever evaluates).
**Documented decision:** which use cases cut across the other views and validate them. If a scenario cannot be told with the elements and diagrams of the other views, something is missing in them.

| Scenario | Stories | Tests |
|---|---|---|
| SC-01 · Visitor searches for and books a slot | US16, US17, US18, US01, US03, US22 | see the [table](scenarios.md#scenarios-stories-diagrams-and-tests) |
| SC-02 · Owner sets up club, pitches, schedules and prices | US02, US06, US07, US08, US09, US10 | same |
| SC-03 · Owner cancels a booking in the calendar | US11, US12, US22 | same |
| SC-04 · Weekly booking with one date skipped | US20, US18 | same |
| SC-05 · Password recovery | US05 | same |
| SC-06 · Admin deactivates an owner | US29, US04 | same |
| SC-07 · Race for the same slot | T03, US18 | same |
