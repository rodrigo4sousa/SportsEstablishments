import { describe, expect, it } from "vitest";
import { formatCount, formatHours, formatOccupation } from "./format";
import { pitchFromParams, rangeFromParams, statisticsHref } from "./statistics-url";
import { defaultRange } from "./components/owner-statistics";

const text = (value: string) => value.replace(/\s/g, " ");

describe("pt-PT formatting of the statistics", () => {
  it.each([
    [0, "0 h"],
    [4, "4 h"],
    [4.5, "4,5 h"],
    [3.33, "3,33 h"],
    [1234.5, "1234,5 h"], // pt-PT does not group four digits
    [12345.5, "12 345,5 h"],
  ])("writes %s hours as «%s»", (value, expected) => {
    expect(text(formatHours(value, "pt-PT"))).toBe(expected);
  });

  it.each([
    [0, "0 %"],
    [0.75, "75 %"],
    [0.4091, "40,9 %"],
    [0.8125, "81,3 %"],
    [1, "100 %"],
  ])("writes the occupation %s as «%s»", (value, expected) => {
    expect(text(formatOccupation(value, "pt-PT"))).toBe(expected);
  });

  it("groups thousands of bookings like pt-PT", () => {
    expect(text(formatCount(12345, "pt-PT"))).toBe("12 345");
    expect(formatCount(7, "pt-PT")).toBe("7");
  });
});

describe("English formatting of the statistics (T09)", () => {
  it("writes hours, occupation and counts the en-GB way", () => {
    expect(formatHours(4.5, "en")).toBe("4.5 h");
    expect(formatOccupation(0.8125, "en")).toBe("81.3%");
    expect(formatCount(12345, "en")).toBe("12,345");
  });
});

describe("the statistics URL", () => {
  it("has no period without dates, and keeps only real dates", () => {
    expect(rangeFromParams(new URLSearchParams(""))).toEqual({ from: null, to: null });
    expect(rangeFromParams(new URLSearchParams("from=2026-05-04&to=2026-05-10"))).toEqual({ from: "2026-05-04", to: "2026-05-10" });
    expect(rangeFromParams(new URLSearchParams("from=ontem&to=2026-02-30"))).toEqual({ from: null, to: null });
  });

  it("keeps a pitch only if it is an id", () => {
    expect(pitchFromParams(new URLSearchParams("pitch=0199f7a0-0000-7000-8000-0000000000d1"))).toBe("0199f7a0-0000-7000-8000-0000000000d1");
    expect(pitchFromParams(new URLSearchParams("pitch=campo-1"))).toBeNull();
    expect(pitchFromParams(new URLSearchParams(""))).toBeNull();
  });

  it("writes only what is set", () => {
    expect(statisticsHref("/manage/statistics", { from: null, to: null })).toBe("/manage/statistics");
    expect(statisticsHref("/manage/statistics", { from: "2026-05-04", to: null })).toBe("/manage/statistics?from=2026-05-04");
    expect(statisticsHref("/x", { from: "2026-05-04", to: "2026-05-10" }, "0199f7a0-0000-7000-8000-0000000000d1")).toBe(
      "/x?from=2026-05-04&to=2026-05-10&pitch=0199f7a0-0000-7000-8000-0000000000d1",
    );
  });

  it("the default period is the last 30 days in Lisbon, today included", () => {
    // 23:30 UTC on 13 May 2026 is already 14 May in Lisbon.
    expect(defaultRange(new Date("2026-05-13T23:30:00Z"))).toEqual({ from: "2026-04-15", to: "2026-05-14" });
    expect(defaultRange(new Date("2026-05-13T10:00:00Z"))).toEqual({ from: "2026-04-14", to: "2026-05-13" });
  });
});
