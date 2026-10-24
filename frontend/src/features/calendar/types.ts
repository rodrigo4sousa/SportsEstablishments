import type { components } from "@/lib/api/schema";

type Schemas = components["schemas"];

/** The generated types allow numbers as strings (JSON number formats); the API sends numbers. */
export type CalendarBooking = Omit<Schemas["CalendarBooking"], "price"> & { price: number };

export type CalendarBlock = Schemas["CalendarBlock"];

export type CalendarDay = Schemas["CalendarDay"];

export type CalendarPitch = Omit<Schemas["CalendarPitch"], "slotDurationMinutes"> & { slotDurationMinutes: number };

/** What the calendar API answers for a range of up to seven days (one day for the daily view, a week for the weekly one). */
export type CalendarRange = {
  from: string;
  to: string;
  pitches: CalendarPitch[];
  bookings: CalendarBooking[];
  blocks: CalendarBlock[];
};

/** A booking as the owner sees it when opening it: the player's name is the only personal data. */
export type ClubBooking = Omit<Schemas["ClubBookingResponse"], "price"> & { price: number };

export function toCalendarRange(response: Schemas["CalendarResponse"]): CalendarRange {
  return {
    ...response,
    pitches: response.pitches.map((pitch) => ({ ...pitch, slotDurationMinutes: Number(pitch.slotDurationMinutes) })),
    bookings: response.bookings.map((booking) => ({ ...booking, price: Number(booking.price) })),
  };
}

export function toClubBooking(booking: Schemas["ClubBookingResponse"]): ClubBooking {
  return { ...booking, price: Number(booking.price) };
}
