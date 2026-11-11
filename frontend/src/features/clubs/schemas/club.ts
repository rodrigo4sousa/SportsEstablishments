import { z } from "zod";
import { EMAIL_MAX_LENGTH, PHONE_PATTERN } from "@/features/auth/schemas/register";
import type { Translator } from "@/i18n/translator";

/**
 * US06 club rules, for immediate feedback only: the API validates again and has the last word.
 * Limits match the API's ClubRequestValidator (the limits are provisional, see US06); the messages are the catalogs'.
 */
export const CLUB_NAME_MAX_LENGTH = 100;
export const CLUB_ADDRESS_MAX_LENGTH = 200;
export const CLUB_LOCALITY_MAX_LENGTH = 100;
export const DEFAULT_CANCELLATION_WINDOW_HOURS = 24;
export const MIN_CANCELLATION_WINDOW_HOURS = 1;
export const MAX_CANCELLATION_WINDOW_HOURS = 168;

/** `t` reads `clubs.validation`; `email` is the shared `validation` namespace (the email rule is the same everywhere). */
export function createClubSchema(t: Translator<"clubs.validation">, email: Translator<"validation">) {
  const invalidEmail = email("emailInvalid");
  const invalidWindow = t("windowInvalid", { min: MIN_CANCELLATION_WINDOW_HOURS, max: MAX_CANCELLATION_WINDOW_HOURS });
  return z.object({
    name: z
      .string()
      .trim()
      .min(1, t("nameRequired"))
      .max(CLUB_NAME_MAX_LENGTH, t("nameMax", { max: CLUB_NAME_MAX_LENGTH })),
    address: z
      .string()
      .trim()
      .min(1, t("addressRequired"))
      .max(CLUB_ADDRESS_MAX_LENGTH, t("addressMax", { max: CLUB_ADDRESS_MAX_LENGTH })),
    locality: z
      .string()
      .trim()
      .min(1, t("localityRequired"))
      .max(CLUB_LOCALITY_MAX_LENGTH, t("localityMax", { max: CLUB_LOCALITY_MAX_LENGTH })),
    // Spaces are allowed while typing ("912 345 678") and removed, like the API does.
    phone: z
      .string()
      .transform((phone) => phone.replaceAll(" ", "").trim())
      .pipe(z.string().min(1, t("phoneRequired")).regex(PHONE_PATTERN, t("phoneInvalid"))),
    contactEmail: z.string().trim().min(1, t("emailRequired")).max(EMAIL_MAX_LENGTH, invalidEmail).pipe(z.email(invalidEmail)),
    // A text field in the form (what was typed); a whole number of hours once valid.
    cancellationWindowHours: z
      .string()
      .trim()
      .min(1, t("windowRequired"))
      .regex(/^\d+$/, invalidWindow)
      .transform(Number)
      .pipe(z.number().int().min(MIN_CANCELLATION_WINDOW_HOURS, invalidWindow).max(MAX_CANCELLATION_WINDOW_HOURS, invalidWindow)),
    // US23: the email to the owner on each new booking made by a player; on unless the owner turns it off.
    notifyOnNewBooking: z.boolean(),
  });
}

export type ClubFormValues = z.input<ReturnType<typeof createClubSchema>>;
export type ClubFormData = z.output<ReturnType<typeof createClubSchema>>;
