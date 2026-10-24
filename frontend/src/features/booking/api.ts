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
