import { z } from "zod";
import { PASSWORD_MIN_LENGTH } from "./register";

/**
 * US05 rules for the new password, for immediate feedback only: the API validates again and has the last word. The rule is the
 * one of US01 (at least 8 characters, nothing else); the confirmation only guards against a typing slip and is never sent.
 */
export const resetPasswordSchema = z
  .object({
    newPassword: z.string().min(PASSWORD_MIN_LENGTH, `A palavra-passe tem de ter pelo menos ${PASSWORD_MIN_LENGTH} caracteres.`),
    confirmation: z.string().min(1, "Repete a nova palavra-passe."),
  })
  .refine((values) => values.newPassword === values.confirmation, {
    path: ["confirmation"],
    message: "As palavras-passe não coincidem.",
  });

export type ResetPasswordValues = z.input<typeof resetPasswordSchema>;
export type ResetPasswordData = z.output<typeof resetPasswordSchema>;
