import { z } from "zod";
import type { Translator } from "@/i18n/translator";

/**
 * US01 registration rules, for immediate feedback only: the API validates again and has the last word.
 * Limits match the API's RegisterAccountRequestValidator; the messages are the catalogs' (`validation.*`), in the page's language.
 */
export const NAME_MAX_LENGTH = 100;
export const EMAIL_MAX_LENGTH = 256;
export const PASSWORD_MIN_LENGTH = 8;

export const PHONE_PATTERN = /^\+?[0-9]{9,15}$/;

/** The email rule shared by registration and password recovery: required, at most 256 characters, well formed. */
export function emailField(t: Translator<"validation">) {
  const invalid = t("emailInvalid");
  return z.string().trim().min(1, t("emailRequired")).max(EMAIL_MAX_LENGTH, invalid).pipe(z.email(invalid));
}

/** `termsNotAccepted` is the message of the API code `terms_not_accepted`, so the form and the API say the same thing. */
export function createRegisterSchema(t: Translator<"validation">, termsNotAccepted: string) {
  return z.object({
    name: z
      .string()
      .trim()
      .min(1, t("nameRequired"))
      .max(NAME_MAX_LENGTH, t("nameMax", { max: NAME_MAX_LENGTH })),
    email: emailField(t),
    password: z.string().min(PASSWORD_MIN_LENGTH, t("passwordMin", { min: PASSWORD_MIN_LENGTH })),
    // Optional: blank becomes null; spaces are allowed while typing ("912 345 678") and removed.
    phone: z
      .string()
      .transform((phone) => phone.replaceAll(" ", "").trim())
      .refine((phone) => phone === "" || PHONE_PATTERN.test(phone), t("phoneInvalid"))
      .transform((phone) => (phone === "" ? null : phone)),
    // T07: the API stores the version, so the form only has to know that the box is ticked.
    acceptTerms: z.boolean().refine(Boolean, termsNotAccepted),
  });
}

type RegisterSchema = ReturnType<typeof createRegisterSchema>;
export type RegisterFormValues = z.input<RegisterSchema>;
export type RegisterFormData = z.output<RegisterSchema>;
