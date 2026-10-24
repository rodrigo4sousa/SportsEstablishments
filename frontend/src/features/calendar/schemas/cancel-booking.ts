import { z } from "zod";

/**
 * US12 rules for the reason an owner gives when cancelling a booking, for immediate feedback only: the API validates
 * again and has the last word. The messages match the API's `CancelClubBookingRequestValidator`.
 */
export const CANCEL_REASON_MAX_LENGTH = 200;

export const CANCEL_MESSAGES = {
  required: "Indica o motivo do cancelamento.",
  tooLong: `O motivo pode ter no máximo ${CANCEL_REASON_MAX_LENGTH} caracteres.`,
} as const;

export const cancelBookingSchema = z.object({
  reason: z.string().trim().min(1, CANCEL_MESSAGES.required).max(CANCEL_REASON_MAX_LENGTH, CANCEL_MESSAGES.tooLong),
});

export type CancelBookingValues = z.input<typeof cancelBookingSchema>;
export type CancelBookingData = z.output<typeof cancelBookingSchema>;
