# 0006 · GUID v7 public ids

- **Status:** Accepted.
- **Date:** 2026-10-30 (record; the decision comes from the [Stack](../../stack.md), "Code rules").
- **Elements:** D-CLUB, D-PITCH, D-BLOCK, D-PRICEBAND, D-BOOKING, D-USER, D-REFRESH, D-OUTBOX, D-STATUSCHG, C-DB.

## Context

Ids appear in addresses (`/manage/clubs/{id}`), in API responses and in links. A sequential number would reveal how many bookings or clubs exist and would be easy to guess; a random GUID (v4) cannot be guessed, but it scatters inserts across the primary key index.

Alternatives:

1. **Sequential integers (identity).** Compact and ordered, but guessable and assigned by the database.
2. **GUID v4** (`Guid.NewGuid()`). They cannot be guessed, but the indexes fragment.
3. **GUID v7** (`Guid.CreateVersion7()`): random, with the time in the first bits.

## Decision

Option 3: business and account entities are born with a `Guid.CreateVersion7()` generated in code (`Club`, `Pitch`, `Block`, `PitchPriceBand`, `Booking.ForPlayer`/`ForCustomer`, `OutboxMessage`, `RefreshToken`, `AppUser`, `OwnerStatusChange`, and the `SeriesId` of a weekly series). There are no database-generated ids.

## Consequences

**Good**

- They do not reveal how many entities exist and cannot be guessed (a GUID is not, however, an authorization: ownership is still checked, see [ADR 0005](0005-single-marketplace.md)).
- They sort roughly by creation, so indexes grow at the end.
- The entity has its id before it is saved, which lets objects be linked (for example a booking and its outbox message) without going to the database.

**Bad or to watch**

- 16 bytes per key instead of 4 or 8, and on every foreign key.
- Ids contain the creation instant, so a public id reveals when the entity was created. It is not a secret, but an id must not be used as if it were a token.
- An id that is not a valid GUID is refused early (404 on the pages, ignored in the calendar addresses).
