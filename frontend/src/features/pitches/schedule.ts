import { addDaysToDateKey, lisbonToday } from "@/lib/datetime";

/** The week as the owner sees it, Monday first: the API's keys (US08). The day names are in the catalogs (`weekdays.*`). */
export const WEEKDAYS = [
  { key: "monday" },
  { key: "tuesday" },
  { key: "wednesday" },
  { key: "thursday" },
  { key: "friday" },
  { key: "saturday" },
  { key: "sunday" },
] as const;

export type WeekdayKey = (typeof WEEKDAYS)[number]["key"];

export const SLOT_DURATIONS = [30, 60, 90] as const;

/** Opening and closing times go in steps of 30 minutes (provisional, see US08). */
export const TIME_STEP_MINUTES = 30;

/** Bookings, and so the preview, go from today up to 30 days ahead (Lisbon dates). */
export const BOOKING_DAYS_AHEAD = 30;

/** The first and last date the preview accepts, as "YYYY-MM-DD" in Lisbon. */
export function previewDateRange(now: Date = new Date()): { min: string; max: string } {
  const min = lisbonToday(now);
  return { min, max: addDaysToDateKey(min, BOOKING_DAYS_AHEAD) };
}
