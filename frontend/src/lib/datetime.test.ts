import { afterEach, describe, expect, it } from "vitest";
import {
  formatDateKey,
  formatDateKeyMonth,
  formatDateKeyShort,
  lisbonNowLocal,
  addDaysToDateKey,
  isDateKey,
  formatLisbonDate,
  formatLisbonDateTime,
  formatDateKeyLong,
  formatLisbonLongDate,
  formatLisbonDayHeading,
  formatLisbonSlotTime,
  formatLisbonTime,
  lisbonRepeatedHour,
  lisbonDateKey,
  lisbonToday,
  lisbonInstant,
  lisbonDayStart,
  lisbonDayMinutes,
  lisbonLocalMinutes,
  lisbonInstantAtMinute,
  minutesFromLisbonDayStart,
  weekdayIndex,
  startOfWeekDateKey,
} from "./datetime";

// The catalog's words (pt-PT), as `useFormats` passes them.
const LABELS = { summer: "(verão)", winter: "(inverno)" };
const DAYS = { today: "Hoje", tomorrow: "Amanhã", yesterday: "Ontem" };

const originalTimeZone = process.env.TZ;

// Browser time zones far from Lisbon, on both sides of UTC.
const browserTimeZones = [
  { zone: "UTC", januaryOffsetMinutes: 0 },
  { zone: "America/New_York", januaryOffsetMinutes: 300 },
  { zone: "Asia/Tokyo", januaryOffsetMinutes: -540 },
  { zone: "Pacific/Kiritimati", januaryOffsetMinutes: -840 },
];

function useBrowserTimeZone(zone: string, januaryOffsetMinutes: number) {
  process.env.TZ = zone;
  // Guard: the test only means something if the process time zone really changed.
  expect(new Date(2026, 0, 15).getTimezoneOffset()).toBe(januaryOffsetMinutes);
}

afterEach(() => {
  process.env.TZ = originalTimeZone;
});

describe.each(browserTimeZones)("in a browser set to $zone", ({ zone, januaryOffsetMinutes }) => {
  it("shows winter instants in WET (UTC+0)", () => {
    useBrowserTimeZone(zone, januaryOffsetMinutes);

    expect(formatLisbonDateTime("2026-01-15T09:00:00Z")).toBe("15/01/2026 09:00");
  });

  it("shows summer instants in WEST (UTC+1)", () => {
    useBrowserTimeZone(zone, januaryOffsetMinutes);

    expect(formatLisbonDateTime("2026-07-15T09:00:00Z")).toBe("15/07/2026 10:00");
  });

  it("jumps from 01:00 to 02:00 when clocks go forward on 29 March 2026", () => {
    useBrowserTimeZone(zone, januaryOffsetMinutes);

    expect(formatLisbonTime("2026-03-29T00:59:00Z")).toBe("00:59");
    expect(formatLisbonTime("2026-03-29T01:00:00Z")).toBe("02:00");
    expect(formatLisbonTime("2026-03-29T01:30:00Z")).toBe("02:30");
  });

  it("repeats 01:00-02:00 when clocks go back on 25 October 2026", () => {
    useBrowserTimeZone(zone, januaryOffsetMinutes);

    expect(formatLisbonTime("2026-10-25T00:30:00Z")).toBe("01:30"); // WEST
    expect(formatLisbonTime("2026-10-25T01:30:00Z")).toBe("01:30"); // WET
    expect(formatLisbonTime("2026-10-25T02:00:00Z")).toBe("02:00");
  });

  it("labels the repeated hour on 25 October 2026 as verão then inverno", () => {
    useBrowserTimeZone(zone, januaryOffsetMinutes);

    expect(formatLisbonSlotTime("2026-10-25T00:00:00Z", LABELS)).toBe("01:00 (verão)");
    expect(formatLisbonSlotTime("2026-10-25T00:30:00Z", LABELS)).toBe("01:30 (verão)");
    expect(formatLisbonSlotTime("2026-10-25T01:00:00Z", LABELS)).toBe("01:00 (inverno)");
    expect(formatLisbonSlotTime("2026-10-25T01:30:00Z", LABELS)).toBe("01:30 (inverno)");
  });

  it("uses the Lisbon date around midnight", () => {
    useBrowserTimeZone(zone, januaryOffsetMinutes);

    // 23:30 UTC on 31 July is 00:30 on 1 August in Lisbon.
    expect(lisbonDateKey("2026-07-31T23:30:00Z")).toBe("2026-08-01");
    expect(formatLisbonDate("2026-07-31T23:30:00Z")).toBe("01/08/2026");
    // In winter Lisbon and UTC share the date.
    expect(lisbonDateKey("2026-12-31T23:30:00Z")).toBe("2026-12-31");
    expect(lisbonToday(new Date("2026-07-31T22:59:59Z"))).toBe("2026-07-31");
    expect(lisbonToday(new Date("2026-07-31T23:00:00Z"))).toBe("2026-08-01");
  });
});

describe("repeated hour label", () => {
  it("is absent outside the repeated hour, including right before and after it", () => {
    expect(lisbonRepeatedHour("2026-10-24T23:59:00Z")).toBeNull(); // 00:59 WEST
    expect(lisbonRepeatedHour("2026-10-25T02:00:00Z")).toBeNull(); // 02:00 WET
    expect(lisbonRepeatedHour("2026-07-15T00:30:00Z")).toBeNull();
    expect(formatLisbonSlotTime("2026-07-15T09:00:00Z", LABELS)).toBe("10:00");
  });

  it("is absent on the spring-forward day, where no hour repeats", () => {
    expect(lisbonRepeatedHour("2026-03-29T00:30:00Z")).toBeNull(); // 00:30 WET
    expect(lisbonRepeatedHour("2026-03-29T01:00:00Z")).toBeNull(); // 02:00 WEST
  });

  it("tells apart the two slots that share a wall-clock time", () => {
    // The 60-minute slot 00:00–01:00 UTC runs from 01:00 WEST to 01:00 WET.
    expect(`${formatLisbonSlotTime("2026-10-25T00:00:00Z", LABELS)} – ${formatLisbonSlotTime("2026-10-25T01:00:00Z", LABELS)}`).toBe(
      "01:00 (verão) – 01:00 (inverno)",
    );
  });
});

describe("Lisbon formatting", () => {
  it("formats a long date in Portuguese", () => {
    expect(formatLisbonLongDate("2026-08-01T10:00:00Z", "pt-PT")).toBe("sábado, 1 de agosto");
  });

  it("accepts instants with an explicit offset and Date objects", () => {
    expect(formatLisbonTime("2026-07-15T10:00:00+01:00")).toBe("10:00");
    expect(formatLisbonTime(new Date("2026-07-15T09:00:00Z"))).toBe("10:00");
  });

  it("rejects strings without a time zone, which the browser would read as local time", () => {
    expect(() => formatLisbonTime("2026-07-15T10:00:00")).toThrow(/time zone/);
    expect(() => formatLisbonTime("2026-07-15")).toThrow(/time zone/);
  });
});

describe("formatDateKeyLong", () => {
  it.each([
    ["2026-08-01", "sábado, 1 de agosto"],
    ["2026-10-25", "domingo, 25 de outubro"], // the day clocks go back
    ["2026-03-29", "domingo, 29 de março"], // the day clocks go forward
    ["2026-12-31", "quinta-feira, 31 de dezembro"],
  ])("writes %s as %s, whatever the browser's time zone", (dateKey, expected) => {
    for (const zone of ["UTC", "America/New_York", "Pacific/Kiritimati"]) {
      process.env.TZ = zone;
      expect(formatDateKeyLong(dateKey, "pt-PT")).toBe(expected);
    }
  });

  it("rejects what is not a real date", () => {
    expect(() => formatDateKeyLong("2026-02-30", "pt-PT")).toThrow(/YYYY-MM-DD/);
    expect(() => formatDateKeyLong("amanha", "pt-PT")).toThrow(/YYYY-MM-DD/);
  });
});

describe("date keys", () => {
  it("recognises real calendar dates only", () => {
    expect(isDateKey("2026-10-25")).toBe(true);
    expect(isDateKey("2028-02-29")).toBe(true);
    expect(isDateKey("2026-02-29")).toBe(false);
    expect(isDateKey("2026-2-9")).toBe(false);
    expect(isDateKey("")).toBe(false);
  });

  it("adds days across months, years and the October clock change", () => {
    expect(addDaysToDateKey("2026-10-25", 30)).toBe("2026-11-24");
    expect(addDaysToDateKey("2026-12-20", 30)).toBe("2027-01-19");
    expect(addDaysToDateKey("2026-03-01", -1)).toBe("2026-02-28");
    expect(addDaysToDateKey("2026-10-24", 1)).toBe("2026-10-25");
  });

  it("refuses something that is not a date", () => {
    expect(() => addDaysToDateKey("amanha", 1)).toThrow();
  });
});

describe("lisbonNowLocal", () => {
  it("is the Lisbon date and time, not the UTC or browser ones", () => {
    expect(lisbonNowLocal(new Date("2026-10-24T23:30:00Z"))).toBe("2026-10-25T00:30");
    expect(lisbonNowLocal(new Date("2026-01-15T12:05:00Z"))).toBe("2026-01-15T12:05");
    expect(lisbonNowLocal(new Date("2026-10-25T00:30:00Z"))).toBe("2026-10-25T01:30");
  });
});

describe.each(browserTimeZones)("the Lisbon day as real elapsed time, in a browser set to $zone", ({ zone, januaryOffsetMinutes }) => {
  it("starts a summer day at 23:00 UTC the evening before and a winter day at midnight UTC", () => {
    useBrowserTimeZone(zone, januaryOffsetMinutes);

    expect(lisbonDayStart("2026-07-15").toISOString()).toBe("2026-07-14T23:00:00.000Z");
    expect(lisbonDayStart("2026-01-15").toISOString()).toBe("2026-01-15T00:00:00.000Z");
  });

  it("lasts 25 hours when the clocks go back (2026-10-25) and 23 when they go forward (2026-03-29)", () => {
    useBrowserTimeZone(zone, januaryOffsetMinutes);

    expect(lisbonDayMinutes("2026-10-25")).toBe(25 * 60);
    expect(lisbonDayMinutes("2026-03-29")).toBe(23 * 60);
    expect(lisbonDayMinutes("2026-10-24")).toBe(24 * 60);
    expect(lisbonDayMinutes("2026-10-26")).toBe(24 * 60);
    expect(lisbonDayMinutes("2026-03-28")).toBe(24 * 60);
    expect(lisbonDayMinutes("2026-03-30")).toBe(24 * 60);
  });

  it("starts the long day at 23:00 UTC and the short day at midnight UTC", () => {
    useBrowserTimeZone(zone, januaryOffsetMinutes);

    expect(lisbonDayStart("2026-10-25").toISOString()).toBe("2026-10-24T23:00:00.000Z");
    expect(lisbonDayStart("2026-10-26").toISOString()).toBe("2026-10-26T00:00:00.000Z");
    expect(lisbonDayStart("2026-03-29").toISOString()).toBe("2026-03-29T00:00:00.000Z");
    expect(lisbonDayStart("2026-03-30").toISOString()).toBe("2026-03-29T23:00:00.000Z");
  });

  it("counts elapsed minutes across the repeated hour: both 01:30 are different moments", () => {
    useBrowserTimeZone(zone, januaryOffsetMinutes);

    expect(minutesFromLisbonDayStart("2026-10-25T00:30:00Z", "2026-10-25")).toBe(90); // 01:30 WEST
    expect(minutesFromLisbonDayStart("2026-10-25T01:30:00Z", "2026-10-25")).toBe(150); // 01:30 WET
    expect(minutesFromLisbonDayStart("2026-10-25T09:00:00Z", "2026-10-25")).toBe(10 * 60); // 09:00 WET, one hour more than the clock says
  });

  it("counts elapsed minutes across the missing hour: 02:00 is one hour into the short day", () => {
    useBrowserTimeZone(zone, januaryOffsetMinutes);

    expect(minutesFromLisbonDayStart("2026-03-29T01:00:00Z", "2026-03-29")).toBe(60); // 02:00 WEST
    expect(minutesFromLisbonDayStart("2026-03-29T09:00:00Z", "2026-03-29")).toBe(9 * 60); // 10:00 WEST
  });

  it("is negative before the day and longer than it after the day", () => {
    useBrowserTimeZone(zone, januaryOffsetMinutes);

    expect(minutesFromLisbonDayStart("2026-10-24T22:00:00Z", "2026-10-25")).toBe(-60);
    expect(minutesFromLisbonDayStart("2026-10-26T01:00:00Z", "2026-10-25")).toBe(25 * 60 + 60);
  });

  it("turns a wall-clock time into elapsed minutes, with the first occurrence of a repeated time", () => {
    useBrowserTimeZone(zone, januaryOffsetMinutes);

    expect(lisbonLocalMinutes("2026-10-25", "01:30")).toBe(90);
    expect(lisbonLocalMinutes("2026-10-25", "02:00")).toBe(180); // after both 01:00-02:00 hours
    expect(lisbonLocalMinutes("2026-10-25", "09:00")).toBe(600);
    expect(lisbonLocalMinutes("2026-07-15", "09:30")).toBe(570);
  });

  it("moves a time that does not exist forward past the gap, as the API does: 01:30 becomes 02:30 WEST", () => {
    useBrowserTimeZone(zone, januaryOffsetMinutes);

    expect(lisbonInstant("2026-03-29", "01:30").toISOString()).toBe("2026-03-29T01:30:00.000Z");
    expect(lisbonLocalMinutes("2026-03-29", "01:30")).toBe(90);
    expect(lisbonLocalMinutes("2026-03-29", "03:00")).toBe(120);
  });

  it("gives the instant of a wall-clock time, and the instant of a number of elapsed minutes", () => {
    useBrowserTimeZone(zone, januaryOffsetMinutes);

    expect(lisbonInstant("2026-07-15", "09:30").toISOString()).toBe("2026-07-15T08:30:00.000Z");
    expect(lisbonInstant("2026-01-15", "09:30").toISOString()).toBe("2026-01-15T09:30:00.000Z");
    expect(lisbonInstantAtMinute("2026-10-25", 150).toISOString()).toBe("2026-10-25T01:30:00.000Z");
    expect(lisbonInstantAtMinute("2026-10-25", 150).getTime() - lisbonDayStart("2026-10-25").getTime()).toBe(150 * 60_000);
  });

  it("rejects what is not a date or a time", () => {
    useBrowserTimeZone(zone, januaryOffsetMinutes);

    expect(() => lisbonInstant("2026-02-30")).toThrow();
    expect(() => lisbonInstant("2026-02-20", "25:00")).toThrow();
    expect(() => lisbonInstant("2026-02-20", "9:00")).toThrow();
  });
});

describe("weeks, Monday first", () => {
  it("numbers the weekday from Monday = 0", () => {
    expect(weekdayIndex("2026-05-04")).toBe(0); // Monday
    expect(weekdayIndex("2026-05-07")).toBe(3);
    expect(weekdayIndex("2026-05-10")).toBe(6); // Sunday
    expect(weekdayIndex("2026-10-25")).toBe(6);
    expect(weekdayIndex("2026-03-29")).toBe(6);
  });

  it("finds the Monday of the week, across month and year ends and the clock changes", () => {
    expect(startOfWeekDateKey("2026-05-04")).toBe("2026-05-04");
    expect(startOfWeekDateKey("2026-05-10")).toBe("2026-05-04");
    expect(startOfWeekDateKey("2026-05-11")).toBe("2026-05-11");
    expect(startOfWeekDateKey("2026-10-25")).toBe("2026-10-19"); // the 25-hour Sunday closes its week
    expect(startOfWeekDateKey("2026-03-29")).toBe("2026-03-23"); // the 23-hour Sunday too
    expect(startOfWeekDateKey("2027-01-01")).toBe("2026-12-28");
    expect(startOfWeekDateKey("2026-03-01")).toBe("2026-02-23");
  });

  it("rejects what is not a date", () => {
    expect(() => weekdayIndex("2026-02-30")).toThrow();
  });
});

describe("date key formatting for the admin charts", () => {
  it("writes a date key in pt-PT whatever the time zone", () => {
    expect(formatDateKey("2026-10-25")).toBe("25/10/2026");
    expect(formatDateKeyShort("2026-10-25")).toBe("25/10");
    expect(formatDateKeyMonth("2026-10-01", "pt-PT")).toBe("outubro de 2026");
  });

  it("refuses a value that is not a date", () => {
    expect(() => formatDateKey("2026-02-30")).toThrow();
    expect(() => formatDateKeyShort("hoje")).toThrow();
    expect(() => formatDateKeyMonth("2026-13-01", "pt-PT")).toThrow();
  });
});

describe("the heading of a day in a list of bookings", () => {
  // 30 October 2026, 12:00 in Lisbon (WEST).
  const now = new Date("2026-10-30T12:00:00Z");

  it.each([
    ["2026-10-30T18:00:00Z", "Hoje · sexta-feira, 30 de outubro"],
    ["2026-10-31T09:00:00Z", "Amanhã · sábado, 31 de outubro"],
    ["2026-10-29T09:00:00Z", "Ontem · quinta-feira, 29 de outubro"],
    ["2026-11-04T10:00:00Z", "quarta-feira, 4 de novembro"],
  ])("writes %s as «%s»", (instant, heading) => {
    expect(formatLisbonDayHeading(instant, "pt-PT", DAYS, now)).toBe(heading);
  });

  it("adds the year when it is not this year", () => {
    expect(formatLisbonDayHeading("2025-03-04T10:00:00Z", "pt-PT", DAYS, now)).toBe("terça-feira, 4 de março de 2025");
  });

  it("uses the Lisbon day, not the browser's, for both the booking and now (in summer, when Lisbon is UTC+1)", () => {
    const summer = new Date("2026-07-15T12:00:00Z");
    // 23:30 UTC on the 15th is already 00:30 on the 16th in Lisbon.
    expect(formatLisbonDayHeading("2026-07-15T23:30:00Z", "pt-PT", DAYS, summer)).toBe("Amanhã · quinta-feira, 16 de julho");
    // And someone looking at 23:30 UTC is already on the 16th.
    expect(formatLisbonDayHeading("2026-07-16T10:00:00Z", "pt-PT", DAYS, new Date("2026-07-15T23:30:00Z"))).toBe("Hoje · quinta-feira, 16 de julho");
  });
});

describe("in English (T09): en-GB words, the same Lisbon times", () => {
  const english = { summer: "(summer time)", winter: "(winter time)" };

  it("writes weekdays and months in English", () => {
    expect(formatLisbonLongDate("2026-08-01T10:00:00Z", "en")).toBe("Saturday 1 August");
    expect(formatDateKeyLong("2026-08-01", "en")).toBe("Saturday 1 August");
    expect(formatDateKeyMonth("2026-10-01", "en")).toBe("October 2026");
  });

  it("labels the repeated October hour with the words it is given", () => {
    expect(formatLisbonSlotTime("2026-10-25T00:30:00Z", english)).toBe("01:30 (summer time)");
    expect(formatLisbonSlotTime("2026-10-25T01:30:00Z", english)).toBe("01:30 (winter time)");
  });

  it("heads a day of another year with the year", () => {
    const words = { today: "Today", tomorrow: "Tomorrow", yesterday: "Yesterday" };
    expect(formatLisbonDayHeading("2025-03-04T10:00:00Z", "en", words, new Date("2026-10-30T12:00:00Z"))).toBe("Tuesday, 4 March 2025");
    expect(formatLisbonDayHeading("2026-10-31T09:00:00Z", "en", words, new Date("2026-10-30T12:00:00Z"))).toBe("Tomorrow · Saturday 31 October");
  });
});
