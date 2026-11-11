# 0007 · Slots are calculated, not stored

- **Status:** Accepted.
- **Date:** 2026-10-30 (record; the decision comes from US08 and US17 and from T04).
- **Elements:** D-PITCH, D-OPENDAY, D-PRICEBAND, D-BLOCK, D-BOOKING, D-SLOT, CC-CLUBTIME, CC-DAYCAL, CC-SLOTCHECK, API-SCHED, API-CATALOG, DB-BOOKINGS.

## Context

A pitch has a weekly schedule, a slot duration (30, 60 or 90 minutes), blocks and bookings, and the player books "the free slots" of the next 30 days. One row per slot could be stored, or slots could be generated when they are needed. The `Europe/Lisbon` time zone complicates things: two days of the year have 23 and 25 hours.

Alternatives:

1. **A slots table**, filled in advance (30 days × pitches × slots). It has to be regenerated on every change of schedule, duration or block, and risks getting out of sync with the schedule.
2. **Generate the slots from the schedule when they are needed.**

## Decision

Option 2:

- **Only the facts are stored:** the schedule (`pitch_opening_hours`, local time and weekday), the slot duration, the blocks, the price bands and the bookings, which store the UTC interval `[starts_at, ends_at)` and the price at the time. There is no slots table.
- **One generator:** `Pitch.SlotsOn(date, timeZone)` calls `SlotGenerator`, which cuts the opening interval into blocks of **real elapsed time** from opening; the local↔UTC conversion is only `ClubTime` (the clock-change rules of T04). On clock-change days there is one slot fewer or more, and each lasts exactly its duration.
- **One definition of "free"** (`PitchDayCalendar`, `DaySlot.IsFreeAt`) for the owner preview, the public slots and the search; **one bookable check** (`BookingSlotCheck`) for the player, the owner and the series; **one price** (`Pitch.PriceForSlot`).
- To book, the request carries the start (an instant with a zone); the server has to find it among the generated slots (`SlotStartingAt`, otherwise 422 `invalid_slot`) and the end always comes from there.

## Consequences

**Good**

- **Changing the schedule, the duration or the price bands never touches bookings**: a booking outside the new schedule stays valid (US08), and each one stores its own price.
- Nothing to regenerate or synchronize; the schedule is the single source.
- The calculation is the same everywhere, and the exclusion constraint ([ADR 0003](0003-booking-exclusion-constraint.md)) handles overlaps between bookings regardless of the grid.

**Bad or to watch**

- Each slots query recalculates and joins blocks and bookings (one read of each for all the pitches asked for; the search only calculates for the clubs on the page).
- A change of duration or schedule may leave confirmed bookings that no longer coincide with the new grid; they are not migrated, on purpose.
- A schedule that goes past midnight is not supported (known MVP limitation): the latest closing time is `00:00`.
