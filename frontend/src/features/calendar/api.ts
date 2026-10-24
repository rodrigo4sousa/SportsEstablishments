import { apiJson } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";
import { toCalendarRange, toClubBooking, type CalendarRange, type ClubBooking } from "./types";

/** Everything the owner's calendar of one club reads sits under this key, so one invalidation refreshes the view and the open booking. */
export const calendarQueryKey = (clubId: string) => ["calendar", clubId] as const;
export const calendarRangeQueryKey = (clubId: string, from: string, to: string, includeCancelled: boolean) =>
  [...calendarQueryKey(clubId), "range", from, to, includeCancelled] as const;
export const calendarBookingQueryKey = (clubId: string, bookingId: string) => [...calendarQueryKey(clubId), "booking", bookingId] as const;

/**
 * US11: up to seven Lisbon days of a club (both dates included): its pitches with each day's opening hours, the bookings
 * (with the player's name) and the blocks. One day for the daily view, Monday to Sunday for the weekly one.
 */
export async function fetchCalendarRange(clubId: string, from: string, to: string, includeCancelled: boolean): Promise<CalendarRange> {
  const query = new URLSearchParams({ from, to, includeCancelled: String(includeCancelled) });
  return toCalendarRange(await apiJson<components["schemas"]["CalendarResponse"]>(`/api/manage/clubs/${clubId}/calendar?${query.toString()}`));
}

/** US11: one booking of the club. 404 `club_not_found` (not the owner's club) or `booking_not_found`. */
export async function fetchClubBooking(clubId: string, bookingId: string): Promise<ClubBooking> {
  return toClubBooking(await apiJson<components["schemas"]["ClubBookingResponse"]>(`/api/manage/clubs/${clubId}/bookings/${bookingId}`));
}

/**
 * US12: cancels a booking that has not started, with the owner's reason. Failures are the API's codes: 400
 * `validation_failed` on `reason`, 404 `booking_not_found`, 422 `booking_not_confirmed` and 422 `booking_already_started`.
 */
export async function cancelClubBooking(clubId: string, bookingId: string, reason: string): Promise<ClubBooking> {
  return toClubBooking(
    await apiJson<components["schemas"]["ClubBookingResponse"]>(`/api/manage/clubs/${clubId}/bookings/${bookingId}/cancel`, {
      method: "POST",
      body: JSON.stringify({ reason }),
    }),
  );
}

/** What the owner enters to book a slot for a customer who has no account (US13). */
export type CreateClubBookingRequest = { pitchId: string; startsAt: string; customerName: string; customerPhone: string };

/**
 * US13: books a slot of the club for a customer. Same slot rules as a player's booking: 409 `slot_taken` and `slot_blocked`,
 * 422 `invalid_slot`, `slot_started`, `outside_booking_window`, `pitch_inactive` and `club_unavailable`, 404 `pitch_not_found`,
 * and 400 `validation_failed` on `customerName` and `customerPhone`.
 */
export async function createClubBooking(clubId: string, request: CreateClubBookingRequest): Promise<ClubBooking> {
  return toClubBooking(
    await apiJson<components["schemas"]["ClubBookingResponse"]>(`/api/manage/clubs/${clubId}/bookings`, {
      method: "POST",
      body: JSON.stringify(request),
    }),
  );
}
