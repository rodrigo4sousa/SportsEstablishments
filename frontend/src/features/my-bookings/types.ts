import type { components } from "@/lib/api/schema";

/** The generated types allow numbers as strings (JSON number formats); the API sends numbers. */
export type MyBooking = Omit<components["schemas"]["MyBookingResponse"], "price"> & { price: number };

export const SCOPES = ["upcoming", "past"] as const;

export type BookingScope = (typeof SCOPES)[number];

export type MyBookingsPage = { items: MyBooking[]; page: number; pageSize: number; totalCount: number };

export function toMyBooking(booking: components["schemas"]["MyBookingResponse"]): MyBooking {
  return { ...booking, price: Number(booking.price) };
}
