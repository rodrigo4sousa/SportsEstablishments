import { z } from "zod";

/**
 * US03 login rules, for immediate feedback only: the API validates again. Both fields just need to be filled in;
 * a wrong email or password is the API's generic answer, never a hint about which one was wrong.
 */
export const loginSchema = z.object({
  email: z.string().trim().min(1, "Indica o teu email."),
  // Used exactly as typed (spaces included), like the API does.
  password: z.string().min(1, "Indica a palavra-passe."),
});

export type LoginFormValues = z.input<typeof loginSchema>;
export type LoginFormData = z.output<typeof loginSchema>;
