import { z } from "zod";

/**
 * US01 registration rules, for immediate feedback only: the API validates again and has the last word.
 * Limits and messages match the API's RegisterAccountRequestValidator.
 */
export const NAME_MAX_LENGTH = 100;
export const EMAIL_MAX_LENGTH = 256;
export const PASSWORD_MIN_LENGTH = 8;

const PHONE_PATTERN = /^\+?[0-9]{9,15}$/;
const INVALID_EMAIL = "Indica um email válido, por exemplo nome@exemplo.pt.";

export const registerSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "Indica o teu nome.")
    .max(NAME_MAX_LENGTH, `O nome pode ter no máximo ${NAME_MAX_LENGTH} caracteres.`),
  email: z
    .string()
    .trim()
    .min(1, "Indica o teu email.")
    .max(EMAIL_MAX_LENGTH, INVALID_EMAIL)
    .pipe(z.email(INVALID_EMAIL)),
  password: z
    .string()
    .min(PASSWORD_MIN_LENGTH, `A palavra-passe tem de ter pelo menos ${PASSWORD_MIN_LENGTH} caracteres.`),
  // Optional: blank becomes null; spaces are allowed while typing ("912 345 678") and removed.
  phone: z
    .string()
    .transform((phone) => phone.replaceAll(" ", "").trim())
    .refine((phone) => phone === "" || PHONE_PATTERN.test(phone), "Indica um número de telemóvel válido, com 9 a 15 algarismos.")
    .transform((phone) => (phone === "" ? null : phone)),
});

export type RegisterFormValues = z.input<typeof registerSchema>;
export type RegisterFormData = z.output<typeof registerSchema>;
