import { z } from "zod";
import { DATE_OUT_OF_WINDOW, previewDateRange } from "@/features/pitches/schedule";
import { isDateKey } from "@/lib/datetime";

/**
 * US13 rules for a booking the owner makes for a customer, for immediate feedback only: the API validates again and has
 * the last word. The name and the phone follow the player sign-up (US01), the phone being required here. The messages
 * match the API's `CreateClubBookingRequestValidator`.
 */
export const CUSTOMER_NAME_MAX_LENGTH = 100;

export const NEW_BOOKING_MESSAGES = {
  pitch: "Indica o campo a reservar.",
  slot: "Escolhe um horário livre.",
  nameRequired: "Indica o nome do cliente.",
  nameTooLong: `O nome pode ter no máximo ${CUSTOMER_NAME_MAX_LENGTH} caracteres.`,
  phoneRequired: "Indica o telemóvel do cliente.",
  phoneInvalid: "Indica um número de telemóvel válido, com 9 a 15 algarismos.",
} as const;

const PHONE = /^\+?[0-9]{9,15}$/;

export const newBookingSchema = z.object({
  pitchId: z.string().min(1, NEW_BOOKING_MESSAGES.pitch),
  date: z.string().refine((value) => {
    const range = previewDateRange();
    return isDateKey(value) && value >= range.min && value <= range.max;
  }, DATE_OUT_OF_WINDOW),
  startsAt: z.string().min(1, NEW_BOOKING_MESSAGES.slot),
  customerName: z.string().trim().min(1, NEW_BOOKING_MESSAGES.nameRequired).max(CUSTOMER_NAME_MAX_LENGTH, NEW_BOOKING_MESSAGES.nameTooLong),
  // Spaces are taken out before the check, as the API does ("912 345 678" is fine).
  customerPhone: z
    .string()
    .transform((value) => value.replaceAll(" ", "").trim())
    .pipe(z.string().min(1, NEW_BOOKING_MESSAGES.phoneRequired).regex(PHONE, NEW_BOOKING_MESSAGES.phoneInvalid)),
});

export type NewBookingValues = z.input<typeof newBookingSchema>;
export type NewBookingData = z.output<typeof newBookingSchema>;
