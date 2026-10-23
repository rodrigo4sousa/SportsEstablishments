import { z } from "zod";
import { EMAIL_MAX_LENGTH, PHONE_PATTERN } from "@/features/auth/schemas/register";

/**
 * US06 club rules, for immediate feedback only: the API validates again and has the last word.
 * Limits and messages match the API's ClubRequestValidator (the limits are provisional, see US06).
 */
export const CLUB_NAME_MAX_LENGTH = 100;
export const CLUB_ADDRESS_MAX_LENGTH = 200;
export const CLUB_LOCALITY_MAX_LENGTH = 100;
export const DEFAULT_CANCELLATION_WINDOW_HOURS = 24;
export const MIN_CANCELLATION_WINDOW_HOURS = 1;
export const MAX_CANCELLATION_WINDOW_HOURS = 168;

const INVALID_EMAIL = "Indica um email válido, por exemplo nome@exemplo.pt.";
const INVALID_WINDOW = `A janela de cancelamento tem de estar entre ${MIN_CANCELLATION_WINDOW_HOURS} e ${MAX_CANCELLATION_WINDOW_HOURS} horas.`;

export const clubSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "Indica o nome do clube.")
    .max(CLUB_NAME_MAX_LENGTH, `O nome pode ter no máximo ${CLUB_NAME_MAX_LENGTH} caracteres.`),
  address: z
    .string()
    .trim()
    .min(1, "Indica a morada do clube.")
    .max(CLUB_ADDRESS_MAX_LENGTH, `A morada pode ter no máximo ${CLUB_ADDRESS_MAX_LENGTH} caracteres.`),
  locality: z
    .string()
    .trim()
    .min(1, "Indica a localidade do clube.")
    .max(CLUB_LOCALITY_MAX_LENGTH, `A localidade pode ter no máximo ${CLUB_LOCALITY_MAX_LENGTH} caracteres.`),
  // Spaces are allowed while typing ("912 345 678") and removed, like the API does.
  phone: z
    .string()
    .transform((phone) => phone.replaceAll(" ", "").trim())
    .pipe(
      z
        .string()
        .min(1, "Indica o telefone do clube.")
        .regex(PHONE_PATTERN, "Indica um número de telefone válido, com 9 a 15 algarismos."),
    ),
  contactEmail: z
    .string()
    .trim()
    .min(1, "Indica o email de contacto do clube.")
    .max(EMAIL_MAX_LENGTH, INVALID_EMAIL)
    .pipe(z.email(INVALID_EMAIL)),
  // A text field in the form (what was typed); a whole number of hours once valid.
  cancellationWindowHours: z
    .string()
    .trim()
    .min(1, "Indica a janela de cancelamento, em horas.")
    .regex(/^\d+$/, INVALID_WINDOW)
    .transform(Number)
    .pipe(z.number().int().min(MIN_CANCELLATION_WINDOW_HOURS, INVALID_WINDOW).max(MAX_CANCELLATION_WINDOW_HOURS, INVALID_WINDOW)),
});

export type ClubFormValues = z.input<typeof clubSchema>;
export type ClubFormData = z.output<typeof clubSchema>;
