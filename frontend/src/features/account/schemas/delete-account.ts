import { z } from "zod";

/** US30, for immediate feedback only: the API checks the password. */
export const deleteAccountSchema = z.object({
  // Used exactly as typed (spaces included), like the API does.
  password: z.string().min(1, "Indica a palavra-passe."),
});

export type DeleteAccountValues = z.input<typeof deleteAccountSchema>;
export type DeleteAccountData = z.output<typeof deleteAccountSchema>;
