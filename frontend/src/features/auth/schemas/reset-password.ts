import { z } from "zod";
import type { Translator } from "@/i18n/translator";
import { PASSWORD_MIN_LENGTH } from "./register";

/**
 * US05 rules for the new password, for immediate feedback only: the API validates again and has the last word. The rule is the
 * one of US01 (at least 8 characters, nothing else); the confirmation only guards against a typing slip and is never sent.
 */
export function createResetPasswordSchema(t: Translator<"validation">) {
  return z
    .object({
      newPassword: z.string().min(PASSWORD_MIN_LENGTH, t("passwordMin", { min: PASSWORD_MIN_LENGTH })),
      confirmation: z.string().min(1, t("confirmationRequired")),
    })
    .refine((values) => values.newPassword === values.confirmation, {
      path: ["confirmation"],
      message: t("passwordsDiffer"),
    });
}

export type ResetPasswordValues = z.input<ReturnType<typeof createResetPasswordSchema>>;
export type ResetPasswordData = z.output<ReturnType<typeof createResetPasswordSchema>>;
