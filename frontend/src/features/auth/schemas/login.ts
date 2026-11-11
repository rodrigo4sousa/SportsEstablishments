import { z } from "zod";
import type { Translator } from "@/i18n/translator";

/**
 * US03 login rules, for immediate feedback only: the API validates again. Both fields just need to be filled in;
 * a wrong email or password is the API's generic answer, never a hint about which one was wrong.
 */
export function createLoginSchema(t: Translator<"validation">) {
  return z.object({
    email: z.string().trim().min(1, t("emailRequired")),
    // Used exactly as typed (spaces included), like the API does.
    password: z.string().min(1, t("passwordRequired")),
  });
}

export type LoginFormValues = z.input<ReturnType<typeof createLoginSchema>>;
export type LoginFormData = z.output<ReturnType<typeof createLoginSchema>>;
