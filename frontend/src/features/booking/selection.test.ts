import { describe, expect, it } from "vitest";
import { parseWeeks, resolveSelection, sameInstant, selectionHref } from "./selection";

const pitches = [{ id: "p1" }, { id: "p2" }];
const range = { min: "2026-05-01", max: "2026-05-31" };
const params = (query: string) => new URLSearchParams(query);

describe("resolveSelection", () => {
  it("defaults to the first pitch, today and no slot", () => {
    expect(resolveSelection(params(""), pitches, range)).toEqual({ pitchId: "p1", date: "2026-05-01", dateInWindow: true, slot: null, weeks: 1 });
  });

  it("reads pitch, date and slot from the URL", () => {
    expect(resolveSelection(params("pitch=p2&date=2026-05-04&slot=2026-05-04T10:00:00Z"), pitches, range)).toEqual({
      pitchId: "p2",
      date: "2026-05-04",
      dateInWindow: true,
      slot: "2026-05-04T10:00:00Z",
      weeks: 1,
    });
  });

  it("falls back to the first pitch for one the club does not have", () => {
    expect(resolveSelection(params("pitch=outro"), pitches, range).pitchId).toBe("p1");
    expect(resolveSelection(params(""), [], range).pitchId).toBeNull();
  });

  it("flags a date outside today..+30 days or not a real date, and keeps it", () => {
    for (const date of ["2026-04-30", "2026-06-01", "2026-02-30", "amanha", ""]) {
      const selection = resolveSelection(params(`date=${date}`), pitches, range);
      expect(selection.dateInWindow, date).toBe(false);
      expect(selection.date).toBe(date);
    }
    expect(resolveSelection(params("date=2026-05-01"), pitches, range).dateInWindow).toBe(true);
    expect(resolveSelection(params("date=2026-05-31"), pitches, range).dateInWindow).toBe(true);
  });

  it("ignores a slot that is not an instant with its zone", () => {
    for (const slot of ["", "10:00", "2026-05-04T10:00:00", "2026-05-04", "agora", "2026-05-04T10:00:00Z<script>"]) {
      expect(resolveSelection(params(`slot=${encodeURIComponent(slot)}`), pitches, range).slot, slot).toBeNull();
    }
    expect(resolveSelection(params("slot=2026-10-25T00:00:00Z"), pitches, range).slot).toBe("2026-10-25T00:00:00Z");
    expect(resolveSelection(params("slot=2026-10-25T01:00:00%2B00:00"), pitches, range).slot).toBe("2026-10-25T01:00:00+00:00");
  });
});

describe("selectionHref", () => {
  it("changes some choices and removes others, keeping the rest", () => {
    const current = params("pitch=p1&date=2026-05-04&slot=2026-05-04T10:00:00Z&utm=x");

    expect(selectionHref("/clubs/foz", current, { slot: null })).toBe("/clubs/foz?pitch=p1&date=2026-05-04&utm=x");
    expect(selectionHref("/clubs/foz", current, { pitch: "p2", slot: null })).toBe("/clubs/foz?pitch=p2&date=2026-05-04&utm=x");
  });

  it("writes the whole choice when a slot is picked", () => {
    expect(selectionHref("/clubs/foz", params(""), { pitch: "p1", date: "2026-05-04", slot: "2026-10-25T00:00:00Z" })).toBe(
      "/clubs/foz?pitch=p1&date=2026-05-04&slot=2026-10-25T00%3A00%3A00Z",
    );
  });

  it("is the page itself when nothing is left", () => {
    expect(selectionHref("/clubs/foz", params("slot=2026-05-04T10:00:00Z"), { slot: null })).toBe("/clubs/foz");
  });
});

describe("sameInstant", () => {
  it("compares moments, not text", () => {
    expect(sameInstant("2026-05-04T10:00:00Z", "2026-05-04T11:00:00+01:00")).toBe(true);
    expect(sameInstant("2026-10-25T00:30:00Z", "2026-10-25T01:30:00Z")).toBe(false); // the two 01:30s of that night
  });
});

describe("weeks (US20)", () => {
  it("reads 2 to 4 from the address and takes anything else as no repetition", () => {
    expect(["2", "3", "4"].map((weeks) => resolveSelection(params(`weeks=${weeks}`), pitches, range).weeks)).toEqual([2, 3, 4]);
    for (const weeks of ["", "0", "5", "-1", "2.5", "tres", "10"]) {
      expect(parseWeeks(weeks), weeks).toBe(1);
    }
    expect(parseWeeks(null)).toBe(1);
  });

  it("is kept when another choice changes and removed with null", () => {
    const current = params("pitch=p1&date=2026-05-04&weeks=3");
    expect(selectionHref("/clubs/foz", current, { slot: "2026-05-04T10:00:00Z" })).toBe("/clubs/foz?pitch=p1&date=2026-05-04&weeks=3&slot=2026-05-04T10%3A00%3A00Z");
    expect(selectionHref("/clubs/foz", current, { weeks: null })).toBe("/clubs/foz?pitch=p1&date=2026-05-04");
  });
});
