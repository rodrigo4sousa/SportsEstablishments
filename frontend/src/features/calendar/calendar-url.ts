import { addDaysToDateKey, isDateKey, startOfWeekDateKey } from "@/lib/datetime";
import { isGuid } from "@/lib/guid";

/** The dates the API accepts for the calendar (years 2000 to 2100). */
export const CALENDAR_MIN_DATE = "2000-01-01";
export const CALENDAR_MAX_DATE = "2100-12-31";

export type CalendarMode = "day" | "week";

/** What the calendar shows, as the address holds it: `?view=week&date=2026-10-25&pitch=<id>&cancelled=1&booking=<id>`. */
export type CalendarView = {
  /** One day with every pitch (the default), or one pitch for a week. */
  mode: CalendarMode;
  /** The Lisbon day shown ("YYYY-MM-DD"); in the weekly view, any day of the week shown. */
  date: string;
  /** The pitch of the weekly view; null means the first one. Only kept in the weekly view. */
  pitch: string | null;
  /** Show cancelled bookings too (off by default). */
  includeCancelled: boolean;
  /** The booking whose details are open, if any. */
  booking: string | null;
};

export function isCalendarDate(value: string): boolean {
  return isDateKey(value) && value >= CALENDAR_MIN_DATE && value <= CALENDAR_MAX_DATE;
}

/** Reads the view from the address; a missing or wrong value is the default (daily view of today, no cancelled, nothing open). */
export function viewFromParams(params: URLSearchParams, today: string): CalendarView {
  const date = params.get("date") ?? "";
  const booking = params.get("booking") ?? "";
  const pitch = params.get("pitch") ?? "";
  const mode: CalendarMode = params.get("view") === "week" ? "week" : "day";
  return {
    mode,
    date: isCalendarDate(date) ? date : today,
    pitch: mode === "week" && isGuid(pitch) ? pitch : null,
    includeCancelled: params.get("cancelled") === "1",
    booking: isGuid(booking) ? booking : null,
  };
}

/** The address for a view: only what differs from the default is written (today is not, so «Hoje» is the plain address). */
export function viewHref(pathname: string, view: CalendarView, today: string): string {
  const params = new URLSearchParams();
  if (view.mode === "week") params.set("view", "week");
  if (view.date !== today) params.set("date", view.date);
  if (view.mode === "week" && view.pitch) params.set("pitch", view.pitch);
  if (view.includeCancelled) params.set("cancelled", "1");
  if (view.booking) params.set("booking", view.booking);
  const query = params.toString();
  return query ? `${pathname}?${query}` : pathname;
}

/** The date `days` days away (±1 for a day, ±7 for a week), or null when it falls outside the dates the API accepts. */
export function neighbourDate(date: string, days: number): string | null {
  const next = addDaysToDateKey(date, days);
  return isCalendarDate(next) ? next : null;
}

/** The dates to ask the API for: the day, or the Monday-to-Sunday week the day is in, kept inside the accepted dates. */
export function rangeOf(view: Pick<CalendarView, "mode" | "date">): { from: string; to: string } {
  if (view.mode === "day") {
    return { from: view.date, to: view.date };
  }
  const monday = startOfWeekDateKey(view.date);
  const sunday = addDaysToDateKey(monday, 6);
  return { from: monday < CALENDAR_MIN_DATE ? CALENDAR_MIN_DATE : monday, to: sunday > CALENDAR_MAX_DATE ? CALENDAR_MAX_DATE : sunday };
}
