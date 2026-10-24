import { describe, expect, it } from "vitest";
import { isCalendarDate, neighbourDate, rangeOf, viewFromParams, viewHref } from "./calendar-url";

const TODAY = "2026-05-04";
const BOOKING = "0199f7a0-0000-7000-8000-0000000000b1";
const PITCH = "0199f7a0-0000-7000-8000-0000000000d1";

const params = (query: string) => new URLSearchParams(query);

describe("viewFromParams", () => {
  it("is today, without cancelled bookings and with nothing open, for a plain address", () => {
    expect(viewFromParams(params(""), TODAY)).toEqual({ mode: "day", date: TODAY, pitch: null, includeCancelled: false, booking: null });
  });

  it("reads the date, the toggle and the open booking", () => {
    expect(viewFromParams(params(`date=2026-10-25&cancelled=1&booking=${BOOKING}`), TODAY)).toEqual({
      mode: "day",
      date: "2026-10-25",
      pitch: null,
      includeCancelled: true,
      booking: BOOKING,
    });
  });

  it.each(["", "abc", "2026-02-30", "2026-5-4", "04/05/2026", "1999-12-31", "2101-01-01", "9999-12-31"])("falls back to today for the date «%s»", (date) => {
    expect(viewFromParams(params(`date=${date}`), TODAY).date).toBe(TODAY);
  });

  it.each(["0", "true", "yes", ""])("only «1» turns the cancelled toggle on («%s» does not)", (value) => {
    expect(viewFromParams(params(`cancelled=${value}`), TODAY).includeCancelled).toBe(false);
  });

  it("ignores a booking that is not a GUID", () => {
    expect(viewFromParams(params("booking=../../etc"), TODAY).booking).toBeNull();
  });
});

describe("viewHref", () => {
  const base = { mode: "day" as const, date: TODAY, pitch: null, includeCancelled: false, booking: null };

  it("writes only what differs from the default: today is the plain address", () => {
    expect(viewHref("/manage/clubs/x/calendar", base, TODAY)).toBe("/manage/clubs/x/calendar");
  });

  it("writes the date, the toggle and the booking", () => {
    expect(viewHref("/c", { ...base, date: "2026-10-25", includeCancelled: true, booking: BOOKING }, TODAY)).toBe(`/c?date=2026-10-25&cancelled=1&booking=${BOOKING}`);
  });

  it("round-trips through viewFromParams", () => {
    const view = { ...base, date: "2026-03-29", includeCancelled: true, booking: BOOKING };
    const href = viewHref("/c", view, TODAY);

    expect(viewFromParams(params(href.split("?")[1]), TODAY)).toEqual(view);
  });
});

describe("the weekly view in the address", () => {
  it("reads the view, the pitch and the date", () => {
    expect(viewFromParams(params(`view=week&date=2026-10-25&pitch=${PITCH}`), TODAY)).toEqual({
      mode: "week",
      date: "2026-10-25",
      pitch: PITCH,
      includeCancelled: false,
      booking: null,
    });
  });

  it.each(["day", "", "WEEK", "weekly"])("is the daily view for view=«%s»", (value) => {
    expect(viewFromParams(params(`view=${value}`), TODAY).mode).toBe("day");
  });

  it("ignores a pitch that is not a GUID, and a pitch in the daily view", () => {
    expect(viewFromParams(params("view=week&pitch=../x"), TODAY).pitch).toBeNull();
    expect(viewFromParams(params(`pitch=${PITCH}`), TODAY).pitch).toBeNull();
  });

  it("writes the view and the pitch only for the weekly view, and today is not written", () => {
    const week = { mode: "week" as const, date: TODAY, pitch: PITCH, includeCancelled: false, booking: null };

    expect(viewHref("/c", week, TODAY)).toBe(`/c?view=week&pitch=${PITCH}`);
    expect(viewHref("/c", { ...week, date: "2026-10-25", includeCancelled: true }, TODAY)).toBe(`/c?view=week&date=2026-10-25&pitch=${PITCH}&cancelled=1`);
    expect(viewHref("/c", { ...week, mode: "day" }, TODAY)).toBe("/c");
  });

  it("round-trips through viewFromParams", () => {
    const view = { mode: "week" as const, date: "2026-10-25", pitch: PITCH, includeCancelled: true, booking: BOOKING };

    expect(viewFromParams(params(viewHref("/c", view, TODAY).split("?")[1]), TODAY)).toEqual(view);
  });
});

describe("rangeOf", () => {
  it("is the day itself for the daily view", () => {
    expect(rangeOf({ mode: "day", date: "2026-10-25" })).toEqual({ from: "2026-10-25", to: "2026-10-25" });
  });

  it("is the Monday-to-Sunday week the date is in, for the weekly view (seven days, as the API allows)", () => {
    expect(rangeOf({ mode: "week", date: "2026-05-04" })).toEqual({ from: "2026-05-04", to: "2026-05-10" });
    expect(rangeOf({ mode: "week", date: "2026-05-10" })).toEqual({ from: "2026-05-04", to: "2026-05-10" });
    expect(rangeOf({ mode: "week", date: "2026-10-25" })).toEqual({ from: "2026-10-19", to: "2026-10-25" });
    expect(rangeOf({ mode: "week", date: "2026-03-29" })).toEqual({ from: "2026-03-23", to: "2026-03-29" });
  });

  it("is kept inside the dates the API accepts", () => {
    expect(rangeOf({ mode: "week", date: "2000-01-01" })).toEqual({ from: "2000-01-01", to: "2000-01-02" }); // a Saturday: the Monday was in 1999
    expect(rangeOf({ mode: "week", date: "2100-12-31" })).toEqual({ from: "2100-12-27", to: "2100-12-31" });
  });
});

describe("neighbourDate", () => {
  it("is the day before or after, across month and year ends", () => {
    expect(neighbourDate("2026-03-01", -1)).toBe("2026-02-28");
    expect(neighbourDate("2026-12-31", 1)).toBe("2027-01-01");
    expect(neighbourDate("2026-10-25", 1)).toBe("2026-10-26");
    expect(neighbourDate("2026-10-25", 7)).toBe("2026-11-01");
    expect(neighbourDate("2026-03-29", -7)).toBe("2026-03-22");
  });

  it("stops at the dates the API accepts", () => {
    expect(neighbourDate("2000-01-01", -1)).toBeNull();
    expect(neighbourDate("2100-12-31", 1)).toBeNull();
    expect(isCalendarDate("2000-01-01")).toBe(true);
    expect(isCalendarDate("2100-12-31")).toBe(true);
  });
});
