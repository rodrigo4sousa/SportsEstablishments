import { apiJson } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";
import { toClubBooking, toDayCalendar, type ClubBooking, type DayCalendar } from "./types";

/** Everything the owner's calendar of one club reads sits under this key, so one invalidation refreshes the day and the open booking. */
export const calendarQueryKey = (clubId: string) => ["calendar", clubId] as const;
export const calendarDayQueryKey = (clubId: string, date: string, includeCancelled: boolean) =>
  [...calendarQueryKey(clubId), "day", date, includeCancelled] as const;
export const calendarBookingQueryKey = (clubId: string, bookingId: string) => [...calendarQueryKey(clubId), "booking", bookingId] as const;

/** US11: one Lisbon day of a club: its pitches with the day's opening hours, the bookings (with the player's name) and the blocks. */
export async function fetchCalendarDay(clubId: string, date: string, includeCancelled: boolean): Promise<DayCalendar> {
  const query = new URLSearchParams({ from: date, to: date, includeCancelled: String(includeCancelled) });
  return toDayCalendar(await apiJson<components["schemas"]["CalendarResponse"]>(`/api/manage/clubs/${clubId}/calendar?${query.toString()}`));
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
