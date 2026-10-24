import { apiJson } from "@/lib/api/client";
import type { components } from "@/lib/api/schema";
import { toMyBooking, type BookingScope, type MyBooking, type MyBookingsPage } from "./types";

/** Everything about the player's own bookings sits under this key, so one invalidation refreshes both tabs. */
export const myBookingsQueryKey = ["my-bookings"] as const;
export const myBookingsListQueryKey = (scope: BookingScope, page: number) => [...myBookingsQueryKey, scope, page] as const;

/** US19: the signed-in player's bookings, 20 per page: `upcoming` (confirmed, not ended) or `past` (the rest). */
export async function fetchMyBookings(scope: BookingScope, page: number): Promise<MyBookingsPage> {
  const query = new URLSearchParams({ scope });
  if (page > 1) query.set("page", String(page));
  const response = await apiJson<components["schemas"]["PagedResponseOfMyBookingResponse"]>(`/api/me/bookings?${query.toString()}`);
  return {
    items: response.items.map(toMyBooking),
    page: Number(response.page),
    pageSize: Number(response.pageSize),
    totalCount: Number(response.totalCount),
  };
}

/**
 * US19: cancels one of the player's own bookings. Failures are the API's codes: 404 `booking_not_found`, 422
 * `booking_not_confirmed` and 422 `outside_cancellation_window` (the club's window in force now decides).
 */
export async function cancelBooking(bookingId: string): Promise<MyBooking> {
  return toMyBooking(
    await apiJson<components["schemas"]["MyBookingResponse"]>(`/api/bookings/${encodeURIComponent(bookingId)}/cancel`, { method: "POST" }),
  );
}
