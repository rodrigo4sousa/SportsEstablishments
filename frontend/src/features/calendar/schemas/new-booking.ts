import { z } from "zod";
import { BOOKING_DAYS_AHEAD, previewDateRange } from "@/features/pitches/schedule";
import type { Translator } from "@/i18n/translator";
import { isDateKey } from "@/lib/datetime";

/**
 * US13 rules for a booking the owner makes for a customer, for immediate feedback only: the API validates again and has
 * the last word. The name and the phone follow the player sign-up (US01), the phone being required here. The rules match
 * the API's `CreateClubBookingRequestValidator`; the messages are the catalogs'.
 */
export const CUSTOMER_NAME_MAX_LENGTH = 100;

const PHONE = /^\+?[0-9]{9,15}$/;

export function createNewBookingSchema(t: Translator<"calendar.validation">, dates: Translator<"dates">) {
  return z.object({
    pitchId: z.string().min(1, t("pitch")),
    date: z.string().refine((value) => {
      const range = previewDateRange();
      return isDateKey(value) && value >= range.min && value <= range.max;
    }, dates("outOfWindow", { days: BOOKING_DAYS_AHEAD })),
    startsAt: z.string().min(1, t("slot")),
    customerName: z
      .string()
      .trim()
      .min(1, t("nameRequired"))
      .max(CUSTOMER_NAME_MAX_LENGTH, t("nameTooLong", { max: CUSTOMER_NAME_MAX_LENGTH })),
    // Spaces are taken out before the check, as the API does ("912 345 678" is fine).
    customerPhone: z
      .string()
      .transform((value) => value.replaceAll(" ", "").trim())
      .pipe(z.string().min(1, t("phoneRequired")).regex(PHONE, t("phoneInvalid"))),
  });
}

export type NewBookingValues = z.input<ReturnType<typeof createNewBookingSchema>>;
export type NewBookingData = z.output<ReturnType<typeof createNewBookingSchema>>;
