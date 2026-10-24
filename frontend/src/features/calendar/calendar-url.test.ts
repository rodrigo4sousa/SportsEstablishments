import { describe, expect, it } from "vitest";
import { isCalendarDate, neighbourDate, viewFromParams, viewHref } from "./calendar-url";

const TODAY = "2026-05-04";
const BOOKING = "0199f7a0-0000-7000-8000-0000000000b1";

const params = (query: string) => new URLSearchParams(query);

describe("viewFromParams", () => {
  it("is today, without cancelled bookings and with nothing open, for a plain address", () => {
    expect(viewFromParams(params(""), TODAY)).toEqual({ date: TODAY, includeCancelled: false, booking: null });
  });

  it("reads the date, the toggle and the open booking", () => {
    expect(viewFromParams(params(`date=2026-10-25&cancelled=1&booking=${BOOKING}`), TODAY)).toEqual({
      date: "2026-10-25",
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
  const base = { date: TODAY, includeCancelled: false, booking: null };

  it("writes only what differs from the default: today is the plain address", () => {
    expect(viewHref("/manage/clubs/x/calendar", base, TODAY)).toBe("/manage/clubs/x/calendar");
  });

  it("writes the date, the toggle and the booking", () => {
    expect(viewHref("/c", { date: "2026-10-25", includeCancelled: true, booking: BOOKING }, TODAY)).toBe(`/c?date=2026-10-25&cancelled=1&booking=${BOOKING}`);
  });

  it("round-trips through viewFromParams", () => {
    const view = { date: "2026-03-29", includeCancelled: true, booking: BOOKING };
    const href = viewHref("/c", view, TODAY);

    expect(viewFromParams(params(href.split("?")[1]), TODAY)).toEqual(view);
  });
});

describe("neighbourDate", () => {
  it("is the day before or after, across month and year ends", () => {
    expect(neighbourDate("2026-03-01", -1)).toBe("2026-02-28");
    expect(neighbourDate("2026-12-31", 1)).toBe("2027-01-01");
    expect(neighbourDate("2026-10-25", 1)).toBe("2026-10-26");
  });

  it("stops at the dates the API accepts", () => {
    expect(neighbourDate("2000-01-01", -1)).toBeNull();
    expect(neighbourDate("2100-12-31", 1)).toBeNull();
    expect(isCalendarDate("2000-01-01")).toBe(true);
    expect(isCalendarDate("2100-12-31")).toBe(true);
  });
});
