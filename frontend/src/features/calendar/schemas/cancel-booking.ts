import { z } from "zod";
import type { Translator } from "@/i18n/translator";

/**
 * US12 rules for the reason an owner gives when cancelling a booking, for immediate feedback only: the API validates
 * again and has the last word. The rules match the API's `CancelClubBookingRequestValidator`; the messages are the catalogs'.
 */
export const CANCEL_REASON_MAX_LENGTH = 200;

export function createCancelBookingSchema(t: Translator<"calendar.validation">) {
  return z.object({
    reason: z
      .string()
      .trim()
      .min(1, t("reasonRequired"))
      .max(CANCEL_REASON_MAX_LENGTH, t("reasonTooLong", { max: CANCEL_REASON_MAX_LENGTH })),
  });
}

export type CancelBookingValues = z.input<ReturnType<typeof createCancelBookingSchema>>;
export type CancelBookingData = z.output<ReturnType<typeof createCancelBookingSchema>>;
