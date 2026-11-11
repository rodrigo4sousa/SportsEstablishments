import { apiJson } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";

export type Booking = Omit<components["schemas"]["BookingResponse"], "price"> & { price: number };

/** The slot to book: the pitch and the start as an instant with its zone, exactly as the slot list gave it. */
export type BookingRequest = { pitchId: string; startsAt: string };

/**
 * US18: books one slot. The end and the price come from the pitch, never from here. Failures are the API's codes:
 * 409 `slot_taken` and `slot_blocked`, 422 for a slot that has started, is not a slot, is out of the window, or a
 * pitch or club that no longer takes bookings.
 */
export async function createBooking(request: BookingRequest): Promise<Booking> {
  const booking = await apiJson<components["schemas"]["BookingResponse"]>("/api/bookings", { method: "POST", body: JSON.stringify(request) });
  return { ...booking, price: Number(booking.price) };
}

// ---- US20: the same slot every week

type OccurrenceDto = components["schemas"]["OccurrencePreview"];

/** A date of a weekly series in the preview: `free`, or `unavailable` with a reason code. */
export type Occurrence = { startsAt: string; endsAt: string; price: number; status: "free" | "unavailable"; reason: string | null };

export type RecurringPreview = { weeks: number; occurrences: Occurrence[] };

/** The first slot and how many weeks (1 to 4, the first included). */
export type RecurringRequest = { pitchId: string; startsAt: string; weeks: number };

export type SkippedDate = { startsAt: string; endsAt: string; reason: string };

export type RecurringResult = { seriesId: string; created: Booking[]; skipped: SkippedDate[] };

const toOccurrence = (occurrence: OccurrenceDto): Occurrence => ({
  ...occurrence,
  price: Number(occurrence.price),
  status: occurrence.status === "free" ? "free" : "unavailable",
});

/** Read-only: which dates of the series are free, with each date's price. */
export async function previewRecurring(request: RecurringRequest): Promise<RecurringPreview> {
  const preview = await apiJson<components["schemas"]["RecurringPreviewResponse"]>("/api/bookings/recurring/preview", {
    method: "POST",
    body: JSON.stringify(request),
  });
  return { weeks: Number(preview.weeks), occurrences: preview.occurrences.map(toOccurrence) };
}

/**
 * Books every free date, each as its own booking. 201 with what was booked and the dates left out; when no date could be booked the
 * API answers 409 `series_unavailable`, and {@link skippedOf} reads the dates from the error.
 */
export async function createRecurring(request: RecurringRequest): Promise<RecurringResult> {
  const result = await apiJson<components["schemas"]["RecurringBookingResponse"]>("/api/bookings/recurring", {
    method: "POST",
    body: JSON.stringify(request),
  });
  return { seriesId: result.seriesId, created: result.created.map((booking) => ({ ...booking, price: Number(booking.price) })), skipped: result.skipped };
}

/** The dates a `series_unavailable` error says were left out. */
export function skippedOf(error: { extensions: Readonly<Record<string, unknown>> }): SkippedDate[] {
  const skipped = error.extensions.skipped;
  return Array.isArray(skipped) ? (skipped as SkippedDate[]) : [];
}

/** The reason codes of a date left out of a series (US20); their words are in the catalogs (`booking.recurring.reasons.*`). */
export const OCCURRENCE_REASON_CODES = ["taken", "blocked", "closed", "not_a_slot", "outside_window", "started", "pitch_inactive"] as const;
