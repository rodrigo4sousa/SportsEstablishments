# 0005 · One marketplace instead of one site per owner

- **Status:** Accepted.
- **Date:** 2026-10-30 (record; the decision comes from the [backlog](../../backlog.md), "Decided rules", and from US01 and US02).
- **Elements:** P-JOG, P-DONO, P-ADMIN, P-CLI, D-CLUB, D-USER, API-CATALOG, API-CLUBS, CC-AUTHZ.

## Context

The product serves clubs of several owners. Each owner could have their own site and domain, with their own players, or everyone could share one platform.

Alternatives:

1. **One site (or domain) per owner**, each with its own players. It isolates the owners, but multiplies configuration, certificates and accounts, and forces a player to sign up at every club.
2. **A marketplace**: one application, one database, shared accounts.

## Decision

Option 2, as a decided business rule:

- **A player account works at every club of every owner.** There are no per-owner sites or domains.
- One email = one account = one profile (`Player`, `Owner` or `Admin`). An owner may have several clubs; a club has a single owner, forever (`OwnerId` only at creation).
- The public sees the clubs through the catalogue (`/` and `/clubs/{slug}`); the `slug` is unique, generated from the name at creation and never changes. The owner area uses the public GUID (`/manage/clubs/{id}`).
- Owners are isolated from one another by authorization, not by physical separation: all access to clubs in the owner area starts from `OwnedClubs`, and a club of another owner gives the same 404 as one that does not exist (T02).
- The admin, the platform owner, sees only counts, never players' personal data; the owner sees only the name of the player who booked.
- A deactivated owner (US29) stops appearing on the public pages and receiving new bookings, but bookings already made stay.
- Whoever books by phone or in person has no account: the owner books on their behalf (name and mobile number, US13), and that booking is not in any player's list.

## Consequences

**Good**

- A player signs up once and searches all clubs; there is only one application to configure and deploy.
- The platform's search and statistics (admin) are queries over the same data.

**Bad or to watch**

- All data shares one database: isolation between owners is just code and tests. Every club resource has to go through `OwnedClubs`/`OwnedPitches`/`OwnedBookings`, and `OwnerAreaAccessTests` discovers the `/api/manage` routes to verify it.
- Public responses and admin responses must never carry personal data; every endpoint has a test that looks for a player's name, email and id in the body.
- Registration reveals that an email already has an account (409 `email_taken`), an accepted trade-off in US01.
