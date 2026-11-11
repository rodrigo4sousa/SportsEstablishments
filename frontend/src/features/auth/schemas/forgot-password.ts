import { z } from "zod";
import type { Translator } from "@/i18n/translator";
import { emailField } from "./register";

/** US05: asking for a reset email needs a well-formed address, nothing more (the API validates again). */
export function createForgotPasswordSchema(t: Translator<"validation">) {
  return z.object({ email: emailField(t) });
}

export type ForgotPasswordValues = z.input<ReturnType<typeof createForgotPasswordSchema>>;
export type ForgotPasswordData = z.output<ReturnType<typeof createForgotPasswordSchema>>;
