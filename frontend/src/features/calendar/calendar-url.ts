import { addDaysToDateKey, isDateKey } from "@/lib/datetime";
import { isGuid } from "@/lib/guid";

/** The dates the API accepts for the calendar (years 2000 to 2100). */
export const CALENDAR_MIN_DATE = "2000-01-01";
export const CALENDAR_MAX_DATE = "2100-12-31";

/** What the calendar shows, as the address holds it: `?date=2026-10-25&cancelled=1&booking=<id>`. */
export type CalendarView = {
  /** The Lisbon day shown ("YYYY-MM-DD"). */
  date: string;
  /** Show cancelled bookings too (off by default). */
  includeCancelled: boolean;
  /** The booking whose details are open, if any. */
  booking: string | null;
};

export function isCalendarDate(value: string): boolean {
  return isDateKey(value) && value >= CALENDAR_MIN_DATE && value <= CALENDAR_MAX_DATE;
}

/** Reads the view from the address; a missing or wrong value is the default (today, no cancelled, no booking open). */
export function viewFromParams(params: URLSearchParams, today: string): CalendarView {
  const date = params.get("date") ?? "";
  const booking = params.get("booking") ?? "";
  return {
    date: isCalendarDate(date) ? date : today,
    includeCancelled: params.get("cancelled") === "1",
    booking: isGuid(booking) ? booking : null,
  };
}

/** The address for a view: only what differs from the default is written (today is not, so «Hoje» is the plain address). */
export function viewHref(pathname: string, view: CalendarView, today: string): string {
  const params = new URLSearchParams();
  if (view.date !== today) params.set("date", view.date);
  if (view.includeCancelled) params.set("cancelled", "1");
  if (view.booking) params.set("booking", view.booking);
  const query = params.toString();
  return query ? `${pathname}?${query}` : pathname;
}

/** The day before or after, kept inside the dates the API accepts (null at the edge). */
export function neighbourDate(date: string, days: -1 | 1): string | null {
  const next = addDaysToDateKey(date, days);
  return isCalendarDate(next) ? next : null;
}
