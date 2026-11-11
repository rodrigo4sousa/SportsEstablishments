import { z } from "zod";
import type { Translator } from "@/i18n/translator";

/** US30, for immediate feedback only: the API checks the password. */
export function createDeleteAccountSchema(t: Translator<"validation">) {
  return z.object({
    // Used exactly as typed (spaces included), like the API does.
    password: z.string().min(1, t("passwordRequired")),
  });
}

export type DeleteAccountValues = z.input<ReturnType<typeof createDeleteAccountSchema>>;
export type DeleteAccountData = z.output<ReturnType<typeof createDeleteAccountSchema>>;
