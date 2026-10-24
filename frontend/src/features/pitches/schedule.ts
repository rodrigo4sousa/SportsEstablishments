import { addDaysToDateKey, lisbonToday } from "@/lib/datetime";

/** The week as the owner sees it, Monday first: the API's keys and the pt-PT day names (US08). */
export const WEEKDAYS = [
  { key: "monday", label: "Segunda-feira" },
  { key: "tuesday", label: "Terça-feira" },
  { key: "wednesday", label: "Quarta-feira" },
  { key: "thursday", label: "Quinta-feira" },
  { key: "friday", label: "Sexta-feira" },
  { key: "saturday", label: "Sábado" },
  { key: "sunday", label: "Domingo" },
] as const;

export type WeekdayKey = (typeof WEEKDAYS)[number]["key"];

export const SLOT_DURATIONS = [30, 60, 90] as const;

/** Opening and closing times go in steps of 30 minutes (provisional, see US08). */
export const TIME_STEP_MINUTES = 30;

/** Bookings, and so the preview, go from today up to 30 days ahead (Lisbon dates). */
export const BOOKING_DAYS_AHEAD = 30;

export const DATE_OUT_OF_WINDOW = `Escolhe uma data de hoje até ${BOOKING_DAYS_AHEAD} dias à frente.`;

/** The first and last date the preview accepts, as "YYYY-MM-DD" in Lisbon. */
export function previewDateRange(now: Date = new Date()): { min: string; max: string } {
  const min = lisbonToday(now);
  return { min, max: addDaysToDateKey(min, BOOKING_DAYS_AHEAD) };
}
