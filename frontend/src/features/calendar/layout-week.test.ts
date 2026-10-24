import { describe, expect, it } from "vitest";
import { HOUR_HEIGHT_PX, heightOf, layoutWeek } from "./layout";
import type { CalendarBlock, CalendarBooking, CalendarPitch, CalendarRange } from "./types";

const PADEL = "0199f7a0-0000-7000-8000-0000000000d1";
const TENNIS = "0199f7a0-0000-7000-8000-0000000000d2";

let counter = 0;
function booking(pitchId: string, startsAt: string, endsAt: string, patch: Partial<CalendarBooking> = {}): CalendarBooking {
  counter += 1;
  return { id: `0199f7a0-0000-7000-8000-${String(counter).padStart(12, "0")}`, pitchId, startsAt, endsAt, status: "confirmed", price: 20, bookerName: "Rita Jogadora", createdByClub: false, ...patch };
}

function block(pitchId: string, startsAt: string, endsAt: string): CalendarBlock {
  counter += 1;
  return { id: `0199f7a0-0000-7000-9000-${String(counter).padStart(12, "0")}`, pitchId, startsAt, endsAt, reason: "Manutenção" };
}

/** A pitch open the same hours on the seven days from the Monday given (closed on the weekdays whose index is in `closedOn`). */
function weekPitch(id: string, monday: string, hours: [string, string], closedOn: number[] = []): CalendarPitch {
  const days = Array.from({ length: 7 }, (_, index) => {
    const [year, month, day] = monday.split("-").map(Number);
    const date = new Date(Date.UTC(year, month - 1, day + index)).toISOString().slice(0, 10);
    return closedOn.includes(index) ? { date, opens: null, closes: null } : { date, opens: hours[0], closes: hours[1] };
  });
  return { id, name: id === PADEL ? "Padel 1" : "Ténis 1", sport: "padel", status: "active", slotDurationMinutes: 60, days };
}

function week(monday: string, pitches: CalendarPitch[], bookings: CalendarBooking[] = [], blocks: CalendarBlock[] = []): CalendarRange {
  return { from: monday, to: "unused", pitches, bookings, blocks };
}

const ALL_CLOSED = [0, 1, 2, 3, 4, 5, 6];

describe("layoutWeek", () => {
  describe("an ordinary week (Monday 2026-05-04 to Sunday 2026-05-10)", () => {
    const monday = "2026-05-04";

    it("has seven days, Monday first, in order", () => {
      const layout = layoutWeek(monday, week(monday, [weekPitch(PADEL, monday, ["09:00", "13:00"])]), PADEL);

      expect(layout?.days.map((day) => day.date)).toEqual([
        "2026-05-04",
        "2026-05-05",
        "2026-05-06",
        "2026-05-07",
        "2026-05-08",
        "2026-05-09",
        "2026-05-10",
      ]);
      expect(layout?.pitch.id).toBe(PADEL);
      expect(layout?.clockChangeDays).toEqual([]);
      expect(layout?.days.every((day) => day.dayMinutes === 1440)).toBe(true);
    });

    it("shares one grid of whole hours from the earliest opening of the week to the latest closing", () => {
      const pitch = weekPitch(PADEL, monday, ["09:00", "13:00"]);
      pitch.days[5] = { date: "2026-05-09", opens: "08:00", closes: "20:00" }; // Saturday opens earlier and closes later
      const layout = layoutWeek(monday, week(monday, [pitch]), PADEL);

      expect(layout?.gridStartMinute).toBe(8 * 60);
      expect(layout?.gridEndMinute).toBe(20 * 60);
      expect(layout?.rows).toHaveLength(12);
      expect(layout?.rows[0]).toEqual({ minute: 480, startsAt: "2026-05-04T07:00:00.000Z" }); // 08:00 WEST
      expect(layout?.days[0].open).toEqual({ fromMinute: 540, toMinute: 780 });
      expect(layout?.days[5].open).toEqual({ fromMinute: 480, toMinute: 1200 });
    });

    it("puts each booking and block in the column of its day, and only those of the chosen pitch", () => {
      const tuesday = booking(PADEL, "2026-05-05T09:00:00Z", "2026-05-05T10:00:00Z"); // 10:00-11:00 on Tuesday
      const saturday = booking(PADEL, "2026-05-09T11:00:00Z", "2026-05-09T12:00:00Z"); // 12:00-13:00 on Saturday
      const other = booking(TENNIS, "2026-05-06T09:00:00Z", "2026-05-06T10:00:00Z");
      const held = block(PADEL, "2026-05-07T08:00:00Z", "2026-05-07T09:00:00Z");
      const elsewhere = block(TENNIS, "2026-05-07T08:00:00Z", "2026-05-07T09:00:00Z");
      const pitches = [weekPitch(PADEL, monday, ["09:00", "13:00"]), weekPitch(TENNIS, monday, ["09:00", "13:00"])];

      const layout = layoutWeek(monday, week(monday, pitches, [saturday, other, tuesday], [elsewhere, held]), PADEL);

      expect(layout!.days.map((day) => day.items.map((item) => item.id))).toEqual([[], [tuesday.id], [], [held.id], [], [saturday.id], []]);
      expect(layout!.days[1].items[0]).toMatchObject({ kind: "booking", startMinute: 600, endMinute: 660 });
    });

    it("shows the other pitch when asked for it", () => {
      const other = booking(TENNIS, "2026-05-06T09:00:00Z", "2026-05-06T10:00:00Z");
      const calendar = week(monday, [weekPitch(PADEL, monday, ["09:00", "13:00"]), weekPitch(TENNIS, monday, ["09:00", "13:00"])], [other]);

      expect(layoutWeek(monday, calendar, PADEL)!.days[2].items).toEqual([]);
      expect(layoutWeek(monday, calendar, TENNIS)!.days[2].items.map((item) => item.id)).toEqual([other.id]);
    });

    it("keeps the column of a day the pitch is closed, with no opening range", () => {
      const layout = layoutWeek(monday, week(monday, [weekPitch(PADEL, monday, ["09:00", "13:00"], [6])]), PADEL);

      expect(layout?.days[6].open).toBeNull();
      expect(layout?.days[0].open).not.toBeNull();
      expect(layout?.days).toHaveLength(7);
    });

    it("lays out a booking that spans midnight from Saturday to Sunday on both days", () => {
      const late = booking(PADEL, "2026-05-09T22:30:00Z", "2026-05-09T23:30:00Z"); // 23:30-00:30
      const layout = layoutWeek(monday, week(monday, [weekPitch(PADEL, monday, ["22:00", "00:00"])], [late]), PADEL);

      expect(layout!.days[5].items[0]).toMatchObject({ startMinute: 1410, endMinute: 1440 });
      expect(layout!.days[6].items[0]).toMatchObject({ startMinute: 0, endMinute: 30 });
      expect(layout?.gridStartMinute).toBe(0);
      expect(layout?.gridEndMinute).toBe(1440);
    });

    it("is null for a pitch that is not in the calendar, and for a week with nothing open, booked or blocked", () => {
      expect(layoutWeek(monday, week(monday, [weekPitch(PADEL, monday, ["09:00", "13:00"])]), TENNIS)).toBeNull();
      expect(layoutWeek(monday, week(monday, [weekPitch(PADEL, monday, ["09:00", "13:00"], ALL_CLOSED)]), PADEL)).toBeNull();
    });

    it("still has a grid for a pitch that is closed all week when it has a booking", () => {
      const stray = booking(PADEL, "2026-05-06T08:00:00Z", "2026-05-06T09:00:00Z");

      const layout = layoutWeek(monday, week(monday, [weekPitch(PADEL, monday, ["09:00", "13:00"], ALL_CLOSED)], [stray]), PADEL);

      expect(layout?.days[2].items).toHaveLength(1);
      expect(layout?.days[2].open).toBeNull();
    });

    it("puts overlapping items of a day side by side", () => {
      const first = booking(PADEL, "2026-05-05T09:00:00Z", "2026-05-05T10:00:00Z", { status: "cancelled_by_club" });
      const second = booking(PADEL, "2026-05-05T09:00:00Z", "2026-05-05T10:00:00Z");

      const layout = layoutWeek(monday, week(monday, [weekPitch(PADEL, monday, ["09:00", "13:00"])], [first, second]), PADEL);

      expect(layout!.days[1].items.map((item) => item.lanes)).toEqual([2, 2]);
    });
  });

  describe("the week the clocks go back (Monday 2026-10-19 to Sunday 2026-10-25)", () => {
    const monday = "2026-10-19";

    it("has the 25-hour Sunday as its last day, and says so", () => {
      const layout = layoutWeek(monday, week(monday, [weekPitch(PADEL, monday, ["00:00", "00:00"])]), PADEL);

      expect(layout?.clockChangeDays).toEqual(["2026-10-25"]);
      expect(layout?.days.map((day) => day.dayMinutes)).toEqual([1440, 1440, 1440, 1440, 1440, 1440, 1500]);
      // The grid is as long as the longest day.
      expect(layout?.gridEndMinute).toBe(25 * 60);
      expect(layout?.rows).toHaveLength(25);
      expect(layout?.days[6].open).toEqual({ fromMinute: 0, toMinute: 1500 });
      expect(layout?.days[5].open).toEqual({ fromMinute: 0, toMinute: 1440 });
    });

    it("labels the scale with the clock of an ordinary day (the Monday), not with the repeated hour", () => {
      const layout = layoutWeek(monday, week(monday, [weekPitch(PADEL, monday, ["00:00", "00:00"])]), PADEL);

      expect(layout?.rows.slice(0, 3).map((row) => row.startsAt)).toEqual(["2026-10-18T23:00:00.000Z", "2026-10-19T00:00:00.000Z", "2026-10-19T01:00:00.000Z"]);
    });

    it("tells the two 01:30 of the Sunday apart, an hour apart in the column", () => {
      const first = booking(PADEL, "2026-10-25T00:30:00Z", "2026-10-25T01:00:00Z"); // 01:30 WEST
      const second = booking(PADEL, "2026-10-25T01:30:00Z", "2026-10-25T02:00:00Z"); // 01:30 WET
      const layout = layoutWeek(monday, week(monday, [weekPitch(PADEL, monday, ["09:00", "13:00"])], [first, second]), PADEL);

      const [one, two] = layout!.days[6].items;
      expect(one).toMatchObject({ id: first.id, startMinute: 90, endMinute: 120 });
      expect(two).toMatchObject({ id: second.id, startMinute: 150, endMinute: 180 });
      expect(heightOf(two.startMinute) - heightOf(one.startMinute)).toBe(HOUR_HEIGHT_PX);
    });

    it("places the same wall-clock hour an hour lower on the Sunday than on the Saturday, by elapsed time", () => {
      const saturday = booking(PADEL, "2026-10-24T08:00:00Z", "2026-10-24T09:00:00Z"); // 09:00-10:00 WEST on Saturday
      const sunday = booking(PADEL, "2026-10-25T09:00:00Z", "2026-10-25T10:00:00Z"); // 09:00-10:00 WET on Sunday
      const layout = layoutWeek(monday, week(monday, [weekPitch(PADEL, monday, ["09:00", "13:00"])], [saturday, sunday]), PADEL);

      expect(layout!.days[5].items[0]).toMatchObject({ startMinute: 540 });
      expect(layout!.days[6].items[0]).toMatchObject({ startMinute: 600 });
      // The opening hours shift with the bookings: both are inside their day's open range.
      expect(layout!.days[5].open).toEqual({ fromMinute: 540, toMinute: 780 });
      expect(layout!.days[6].open).toEqual({ fromMinute: 600, toMinute: 840 });
    });

    it("includes the last hour of the Sunday and leaves the Monday after out", () => {
      const last = booking(PADEL, "2026-10-25T23:00:00Z", "2026-10-26T00:00:00Z"); // 23:00-00:00 WET
      const layout = layoutWeek(monday, week(monday, [weekPitch(PADEL, monday, ["00:00", "00:00"])], [last]), PADEL);

      expect(layout!.days[6].items[0]).toMatchObject({ startMinute: 1440, endMinute: 1500 });
    });
  });

  describe("the week the clocks go forward (Monday 2026-03-23 to Sunday 2026-03-29)", () => {
    const monday = "2026-03-23";

    it("has the 23-hour Sunday as its last day", () => {
      const layout = layoutWeek(monday, week(monday, [weekPitch(PADEL, monday, ["00:00", "00:00"])]), PADEL);

      expect(layout?.clockChangeDays).toEqual(["2026-03-29"]);
      expect(layout?.days[6].dayMinutes).toBe(1380);
      expect(layout?.days[6].open).toEqual({ fromMinute: 0, toMinute: 1380 });
      expect(layout?.rows).toHaveLength(24);
    });

    it("lays out the Sunday booking at 02:00 WEST one hour into its day", () => {
      const early = booking(PADEL, "2026-03-29T01:00:00Z", "2026-03-29T02:00:00Z");
      const layout = layoutWeek(monday, week(monday, [weekPitch(PADEL, monday, ["00:00", "00:00"])], [early]), PADEL);

      expect(layout!.days[6].items[0]).toMatchObject({ startMinute: 60, endMinute: 120 });
    });
  });

  it("has only ordinary days in the week after the change (Monday 2026-10-26)", () => {
    const layout = layoutWeek("2026-10-26", week("2026-10-26", [weekPitch(PADEL, "2026-10-26", ["09:00", "13:00"])]), PADEL);

    expect(layout?.clockChangeDays).toEqual([]);
  });
});
