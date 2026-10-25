import { z } from "zod";
import { EMAIL_MAX_LENGTH } from "./register";

/** US05: asking for a reset email needs a well-formed address, nothing more (the API validates again). */
const INVALID_EMAIL = "Indica um email válido, por exemplo nome@exemplo.pt.";

export const forgotPasswordSchema = z.object({
  email: z.string().trim().min(1, "Indica o teu email.").max(EMAIL_MAX_LENGTH, INVALID_EMAIL).pipe(z.email(INVALID_EMAIL)),
});

export type ForgotPasswordValues = z.input<typeof forgotPasswordSchema>;
export type ForgotPasswordData = z.output<typeof forgotPasswordSchema>;
