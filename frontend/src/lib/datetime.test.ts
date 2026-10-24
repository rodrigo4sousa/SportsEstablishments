import { afterEach, describe, expect, it } from "vitest";
import {
  lisbonNowLocal,
  addDaysToDateKey,
  isDateKey,
  formatLisbonDate,
  formatLisbonDateTime,
  formatLisbonLongDate,
  formatLisbonSlotTime,
  formatLisbonTime,
  lisbonRepeatedHourLabel,
  lisbonDateKey,
  lisbonToday,
} from "./datetime";

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

    expect(formatLisbonSlotTime("2026-10-25T00:00:00Z")).toBe("01:00 (verão)");
    expect(formatLisbonSlotTime("2026-10-25T00:30:00Z")).toBe("01:30 (verão)");
    expect(formatLisbonSlotTime("2026-10-25T01:00:00Z")).toBe("01:00 (inverno)");
    expect(formatLisbonSlotTime("2026-10-25T01:30:00Z")).toBe("01:30 (inverno)");
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
    expect(lisbonRepeatedHourLabel("2026-10-24T23:59:00Z")).toBeNull(); // 00:59 WEST
    expect(lisbonRepeatedHourLabel("2026-10-25T02:00:00Z")).toBeNull(); // 02:00 WET
    expect(lisbonRepeatedHourLabel("2026-07-15T00:30:00Z")).toBeNull();
    expect(formatLisbonSlotTime("2026-07-15T09:00:00Z")).toBe("10:00");
  });

  it("is absent on the spring-forward day, where no hour repeats", () => {
    expect(lisbonRepeatedHourLabel("2026-03-29T00:30:00Z")).toBeNull(); // 00:30 WET
    expect(lisbonRepeatedHourLabel("2026-03-29T01:00:00Z")).toBeNull(); // 02:00 WEST
  });

  it("tells apart the two slots that share a wall-clock time", () => {
    // The 60-minute slot 00:00–01:00 UTC runs from 01:00 WEST to 01:00 WET.
    expect(`${formatLisbonSlotTime("2026-10-25T00:00:00Z")} – ${formatLisbonSlotTime("2026-10-25T01:00:00Z")}`).toBe(
      "01:00 (verão) – 01:00 (inverno)",
    );
  });
});

describe("Lisbon formatting", () => {
  it("formats a long date in Portuguese", () => {
    expect(formatLisbonLongDate("2026-08-01T10:00:00Z")).toBe("sábado, 1 de agosto");
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
