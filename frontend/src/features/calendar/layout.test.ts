import { describe, expect, it } from "vitest";
import { HOUR_HEIGHT_PX, heightOf, layoutDay } from "./layout";
import type { CalendarBlock, CalendarBooking, CalendarPitch, DayCalendar } from "./types";

const PADEL = "0199f7a0-0000-7000-8000-0000000000d1";
const TENNIS = "0199f7a0-0000-7000-8000-0000000000d2";

function pitch(id: string, date: string, opens: string | null, closes: string | null, patch: Partial<CalendarPitch> = {}): CalendarPitch {
  return { id, name: id === PADEL ? "Padel 1" : "Ténis 1", sport: "padel", status: "active", slotDurationMinutes: 60, days: [{ date, opens, closes }], ...patch };
}

let counter = 0;
function booking(pitchId: string, startsAt: string, endsAt: string, patch: Partial<CalendarBooking> = {}): CalendarBooking {
  counter += 1;
  return { id: `0199f7a0-0000-7000-8000-${String(counter).padStart(12, "0")}`, pitchId, startsAt, endsAt, status: "confirmed", price: 20, playerName: "Rita Jogadora", ...patch };
}

function block(pitchId: string, startsAt: string, endsAt: string, reason: string | null = "Manutenção"): CalendarBlock {
  counter += 1;
  return { id: `0199f7a0-0000-7000-9000-${String(counter).padStart(12, "0")}`, pitchId, startsAt, endsAt, reason };
}

function calendar(date: string, pitches: CalendarPitch[], bookings: CalendarBooking[] = [], blocks: CalendarBlock[] = []): DayCalendar {
  return { from: date, to: date, pitches, bookings, blocks };
}

describe("layoutDay", () => {
  describe("an ordinary summer day (2026-05-04, WEST, UTC+1)", () => {
    const date = "2026-05-04";

    it("has one row per hour from the earliest opening to the latest closing of the day", () => {
      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "09:00", "13:00"), pitch(TENNIS, date, "10:30", "21:00")]));

      expect(layout).not.toBeNull();
      // 09:00 (local) to 21:00: 12 rows, widened to whole hours.
      expect(layout?.rows).toHaveLength(12);
      expect(layout?.gridStartMinute).toBe(9 * 60);
      expect(layout?.gridEndMinute).toBe(21 * 60);
      expect(layout?.rows[0]).toEqual({ minute: 540, startsAt: "2026-05-04T08:00:00.000Z" });
      expect(layout?.columns.map((column) => column.pitch.id)).toEqual([PADEL, TENNIS]);
    });

    it("rounds the grid out to whole hours when a pitch opens or closes on the half hour", () => {
      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "09:30", "12:30")]));

      expect(layout?.gridStartMinute).toBe(9 * 60);
      expect(layout?.gridEndMinute).toBe(13 * 60);
      expect(layout?.rows).toHaveLength(4);
      expect(layout?.columns[0].open).toEqual({ fromMinute: 570, toMinute: 750 });
    });

    it("places a booking by the minutes elapsed since the start of the Lisbon day, whatever the browser time zone", () => {
      const slot = booking(PADEL, "2026-05-04T09:00:00Z", "2026-05-04T10:00:00Z"); // 10:00-11:00 in Lisbon
      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "09:00", "13:00")], [slot]));

      const [item] = layout!.columns[0].items;
      expect(item).toMatchObject({ kind: "booking", startMinute: 600, endMinute: 660, lane: 0, lanes: 1 });
      expect(heightOf(item.startMinute - layout!.gridStartMinute)).toBe(HOUR_HEIGHT_PX);
      expect(heightOf(item.endMinute - item.startMinute)).toBe(HOUR_HEIGHT_PX);
    });

    it("places a block in the column of its pitch", () => {
      const held = block(TENNIS, "2026-05-04T11:00:00Z", "2026-05-04T11:30:00Z", "Torneio"); // 12:00-12:30
      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "09:00", "13:00"), pitch(TENNIS, date, "09:00", "13:00")], [], [held]));

      expect(layout!.columns[0].items).toEqual([]);
      expect(layout!.columns[1].items).toEqual([expect.objectContaining({ kind: "block", startMinute: 720, endMinute: 750 })]);
    });

    it("treats a closing time of 00:00 as the end of the day, not its start", () => {
      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "18:00", "00:00")]));

      expect(layout?.gridStartMinute).toBe(18 * 60);
      expect(layout?.gridEndMinute).toBe(24 * 60);
      expect(layout?.rows).toHaveLength(6);
      expect(layout?.columns[0].open).toEqual({ fromMinute: 1080, toMinute: 1440 });
    });

    it("marks a closed pitch as having no opening range, and ignores it for the grid", () => {
      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "09:00", "13:00"), pitch(TENNIS, date, null, null)]));

      expect(layout?.columns[1].open).toBeNull();
      expect(layout?.rows).toHaveLength(4);
    });

    it("widens the grid to a booking made outside the opening hours (an older schedule)", () => {
      const early = booking(PADEL, "2026-05-04T06:30:00Z", "2026-05-04T07:30:00Z"); // 07:30-08:30
      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "09:00", "13:00")], [early]));

      expect(layout?.gridStartMinute).toBe(7 * 60);
      expect(layout?.gridEndMinute).toBe(13 * 60);
    });

    it("is null when nothing is open and nothing is booked or blocked", () => {
      expect(layoutDay(date, calendar(date, [pitch(PADEL, date, null, null)]))).toBeNull();
      expect(layoutDay(date, calendar(date, []))).toBeNull();
    });

    it("still has a grid when the pitch is closed but a booking is there", () => {
      const stray = booking(PADEL, "2026-05-04T08:00:00Z", "2026-05-04T09:00:00Z");

      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, null, null)], [stray]));

      expect(layout?.rows).toHaveLength(1);
      expect(layout?.columns[0].open).toBeNull();
      expect(layout?.columns[0].items).toHaveLength(1);
    });

    it("keeps a booking that spans local midnight to the day, as a stretch to its end", () => {
      const late = booking(PADEL, "2026-05-04T22:30:00Z", "2026-05-04T23:30:00Z"); // 23:30-00:30: only the first half hour is on the 4th
      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "22:00", "00:00")], [late]));

      expect(layout!.columns[0].items[0]).toMatchObject({ startMinute: 1410, endMinute: 1440 });
      expect(layout?.gridEndMinute).toBe(1440);
    });

    it("drops what is entirely outside the day", () => {
      const nextDay = booking(PADEL, "2026-05-05T08:00:00Z", "2026-05-05T09:00:00Z");

      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "09:00", "13:00")], [nextDay]));

      expect(layout!.columns[0].items).toEqual([]);
    });

    it("puts overlapping items side by side in lanes, and the others alone", () => {
      const cancelled = booking(PADEL, "2026-05-04T09:00:00Z", "2026-05-04T10:00:00Z", { status: "cancelled_by_club" });
      const rebooked = booking(PADEL, "2026-05-04T09:00:00Z", "2026-05-04T10:00:00Z");
      const later = booking(PADEL, "2026-05-04T11:00:00Z", "2026-05-04T12:00:00Z");
      const touching = booking(PADEL, "2026-05-04T10:00:00Z", "2026-05-04T11:00:00Z");

      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "09:00", "14:00")], [later, rebooked, cancelled, touching]));

      const byId = Object.fromEntries(layout!.columns[0].items.map((item) => [item.id, item]));
      expect([byId[cancelled.id].lane, byId[rebooked.id].lane].sort()).toEqual([0, 1]);
      expect(byId[cancelled.id].lanes).toBe(2);
      expect(byId[rebooked.id].lanes).toBe(2);
      // Touching (half-open) does not overlap: each of the others has the whole width.
      expect(byId[touching.id]).toMatchObject({ lane: 0, lanes: 1 });
      expect(byId[later.id]).toMatchObject({ lane: 0, lanes: 1 });
    });

    it("gives a booking and a block that overlap their own lanes", () => {
      const noShow = booking(PADEL, "2026-05-04T09:00:00Z", "2026-05-04T10:00:00Z", { status: "no_show" });
      const held = block(PADEL, "2026-05-04T09:30:00Z", "2026-05-04T10:30:00Z");

      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "09:00", "13:00")], [noShow], [held]));

      expect(layout!.columns[0].items.map((item) => [item.kind, item.lane, item.lanes])).toEqual([
        ["booking", 0, 2],
        ["block", 1, 2],
      ]);
    });
  });

  describe("the day the clocks go back (2026-10-25, 25 hours)", () => {
    const date = "2026-10-25";
    // Local midnight is 2026-10-24T23:00Z; at 02:00 WEST the clocks go back to 01:00 WET; the next midnight is 2026-10-26T00:00Z.

    it("has 25 rows when a pitch is open all day, one of them for each of the two 01:00 hours", () => {
      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "00:00", "00:00")]));

      expect(layout?.rows).toHaveLength(25);
      expect(layout?.gridStartMinute).toBe(0);
      expect(layout?.gridEndMinute).toBe(25 * 60);
      expect(layout?.rows.slice(0, 4).map((row) => row.startsAt)).toEqual([
        "2026-10-24T23:00:00.000Z", // 00:00 WEST
        "2026-10-25T00:00:00.000Z", // 01:00 WEST (first)
        "2026-10-25T01:00:00.000Z", // 01:00 WET (second)
        "2026-10-25T02:00:00.000Z", // 02:00 WET
      ]);
      expect(layout?.rows.at(-1)?.startsAt).toBe("2026-10-25T23:00:00.000Z"); // 23:00 WET
    });

    it("tells the two 01:30 apart: they are an hour apart in the grid", () => {
      const first = booking(PADEL, "2026-10-25T00:30:00Z", "2026-10-25T01:00:00Z"); // 01:30 WEST
      const second = booking(PADEL, "2026-10-25T01:30:00Z", "2026-10-25T02:00:00Z"); // 01:30 WET
      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "00:00", "00:00")], [second, first]));

      const [firstItem, secondItem] = layout!.columns[0].items;
      expect(firstItem).toMatchObject({ id: first.id, startMinute: 90, endMinute: 120 });
      expect(secondItem).toMatchObject({ id: second.id, startMinute: 150, endMinute: 180 });
      expect(heightOf(secondItem.startMinute) - heightOf(firstItem.startMinute)).toBe(HOUR_HEIGHT_PX);
    });

    it("lays out a daytime booking one hour lower than the clock says, because of the extra hour", () => {
      const nine = booking(PADEL, "2026-10-25T09:00:00Z", "2026-10-25T10:00:00Z"); // 09:00-10:00 WET
      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "09:00", "13:00")], [nine]));

      // 09:00 on the clock is the 10th hour of real elapsed time on this day.
      expect(layout?.gridStartMinute).toBe(10 * 60);
      expect(layout?.gridEndMinute).toBe(14 * 60);
      expect(layout?.columns[0].items[0]).toMatchObject({ startMinute: 600, endMinute: 660 });
      expect(layout?.columns[0].open).toEqual({ fromMinute: 600, toMinute: 840 });
    });

    it("includes the last hour of the day and drops the first booking of the next one", () => {
      const last = booking(PADEL, "2026-10-25T23:00:00Z", "2026-10-26T00:00:00Z"); // 23:00-00:00 WET
      const next = booking(PADEL, "2026-10-26T00:00:00Z", "2026-10-26T01:00:00Z");

      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "00:00", "00:00")], [last, next]));

      expect(layout!.columns[0].items.map((item) => item.id)).toEqual([last.id]);
      expect(layout!.columns[0].items[0]).toMatchObject({ startMinute: 24 * 60, endMinute: 25 * 60 });
    });

    it("is 24 hours the day before and the day after", () => {
      expect(layoutDay("2026-10-24", calendar("2026-10-24", [pitch(PADEL, "2026-10-24", "00:00", "00:00")]))?.rows).toHaveLength(24);
      expect(layoutDay("2026-10-26", calendar("2026-10-26", [pitch(PADEL, "2026-10-26", "00:00", "00:00")]))?.rows).toHaveLength(24);
    });
  });

  describe("the day the clocks go forward (2026-03-29, 23 hours)", () => {
    const date = "2026-03-29";
    // Local midnight is 2026-03-29T00:00Z; at 01:00 WET the clocks go forward to 02:00 WEST; the next midnight is 2026-03-29T23:00Z.

    it("has 23 rows when a pitch is open all day, and no row for the 01:00 hour that does not exist", () => {
      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "00:00", "00:00")]));

      expect(layout?.rows).toHaveLength(23);
      expect(layout?.gridEndMinute).toBe(23 * 60);
      expect(layout?.rows.slice(0, 3).map((row) => row.startsAt)).toEqual([
        "2026-03-29T00:00:00.000Z", // 00:00 WET
        "2026-03-29T01:00:00.000Z", // 02:00 WEST (01:00 does not exist)
        "2026-03-29T02:00:00.000Z", // 03:00 WEST
      ]);
      expect(layout?.rows.at(-1)?.startsAt).toBe("2026-03-29T22:00:00.000Z"); // 23:00 WEST
    });

    it("lays out a booking at 02:00 WEST on the second row and one at 10:00 on the ninth hour of elapsed time", () => {
      const early = booking(PADEL, "2026-03-29T01:00:00Z", "2026-03-29T02:00:00Z"); // 02:00-03:00 WEST
      const ten = booking(PADEL, "2026-03-29T09:00:00Z", "2026-03-29T10:00:00Z"); // 10:00-11:00 WEST
      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "00:00", "00:00")], [early, ten]));

      expect(layout!.columns[0].items[0]).toMatchObject({ startMinute: 60, endMinute: 120 });
      expect(layout!.columns[0].items[1]).toMatchObject({ startMinute: 540, endMinute: 600 });
    });

    it("lays out a daytime opening one hour earlier than the clock says, because an hour is missing", () => {
      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "09:00", "13:00")]));

      expect(layout?.gridStartMinute).toBe(8 * 60);
      expect(layout?.gridEndMinute).toBe(12 * 60);
      expect(layout?.columns[0].open).toEqual({ fromMinute: 480, toMinute: 720 });
    });

    it("includes the last hour of the day and drops the first booking of the next one", () => {
      const last = booking(PADEL, "2026-03-29T22:00:00Z", "2026-03-29T23:00:00Z"); // 23:00-00:00 WEST
      const next = booking(PADEL, "2026-03-29T23:00:00Z", "2026-03-30T00:00:00Z"); // 00:00 on the 30th

      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "00:00", "00:00")], [last, next]));

      expect(layout!.columns[0].items.map((item) => item.id)).toEqual([last.id]);
      expect(layout!.columns[0].items[0]).toMatchObject({ startMinute: 22 * 60, endMinute: 23 * 60 });
    });

    it("moves an opening time that does not exist forward past the gap, like the API", () => {
      // 01:30 does not exist on this day: it becomes 02:30 WEST, 90 minutes into the day.
      const layout = layoutDay(date, calendar(date, [pitch(PADEL, date, "01:30", "05:00")]));

      expect(layout?.columns[0].open).toEqual({ fromMinute: 90, toMinute: 240 });
    });
  });
});
