# 0003 · Exclusion constraint against duplicate bookings

- **Status:** Accepted.
- **Date:** 2026-10-30 (record; the decision comes from the [Stack](../../stack.md) and from T03).
- **Elements:** DB-NOOVERLAP, DB-GIST, DB-BOOKINGS, D-BOOKING, API-BOOK, API-BOOK-STORE, API-CLUBBOOK, CC-LOCK, CC-SLOTCHECK.

## Context

Two players (or a player and the owner, or two dates of a series) can ask for the same slot at the same time. T03 asks for the database to prevent two overlapping confirmed bookings on the same pitch, "and not only validation in the application", and for the conflict to be a 409 and never a 500.

Alternatives:

1. **Check in the application before inserting.** Two requests can check at the same time and both insert.
2. **`SERIALIZABLE` isolation** or an advisory lock per pitch. It works, but the guarantee depends on all the code going through it; a new path that forgets it duplicates bookings.
3. **A PostgreSQL exclusion constraint**, which holds for any write.

## Decision

Option 3: in the `AddBookings` migration, with hand-written SQL and the `btree_gist` extension,

```sql
ALTER TABLE bookings ADD CONSTRAINT bookings_no_overlap
  EXCLUDE USING gist (pitch_id WITH =, tstzrange(starts_at, ends_at) WITH &&)
  WHERE (status = 'Confirmed');
```

- The violation (SQLSTATE `23P01`) is translated by `IBookingStore.TryAddAsync` into 409 `slot_taken` (or the reason `taken` in a series). The code **does not look** for overlaps between bookings before inserting; the constraint has the last word.
- The status is stored as text so that the `WHERE` condition works, and the constraint covers only `Confirmed`: a cancelled booking or a no-show frees the slot.
- Bookings and blocks are in different tables, and the constraint does not cover both. So creating a booking or a block starts by locking the pitch's row (`SELECT … FOR UPDATE`, CC-LOCK) and only then checks the overlap with blocks; the lock also serves cancellation, no-shows and price bands.
- Losing the race is expected: `BookingStore` logs it at Information (pitch and slot, never the player) and not as an error.

## Consequences

**Good**

- The guarantee holds for the three ways of booking (player, series, owner), for undoing a no-show and for any future path.
- The T03 test (20 simultaneous requests for the same slot) runs against a real database and expects exactly one 201 and the rest 409.

**Bad or to watch**

- Undoing a no-show goes back to `Confirmed` and may clash with a booking made in the meantime: the constraint refuses it (409 `slot_taken`), through `TrySaveChangesAsync`.
- The rule exists as hand-written SQL in a migration, and whoever tests needs a real PostgreSQL (Testcontainers; EF's in-memory provider does not have it).
- It requires the `btree_gist` extension in the production PostgreSQL, and that the role that migrates can create it ([ADR 0009](0009-deployment-target.md), condition 3).
