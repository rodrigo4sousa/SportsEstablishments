import { z } from "zod";
import { PRICE_DECIMALS, parsePrice } from "../price";
import { isSportCode } from "../sports";

/**
 * US07 pitch rules, for immediate feedback only: the API validates again and has the last word.
 * Limits and messages match the API's PitchRequestValidator (the limits are provisional, see US07).
 */
export const PITCH_NAME_MAX_LENGTH = 100;

export { PRICE_DECIMALS };

export const pitchSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "Indica o nome do campo.")
    .max(PITCH_NAME_MAX_LENGTH, `O nome pode ter no máximo ${PITCH_NAME_MAX_LENGTH} caracteres.`),
  sport: z
    .string()
    .min(1, "Indica o desporto do campo.")
    .refine(isSportCode, "Escolhe um desporto da lista: futsal, futebol 7, padel ou ténis."),
  // A text field in the form (what was typed, "12,50"); a number of euros once valid.
  pricePerSlot: z.string().transform((text, context) => {
    const price = parsePrice(text);
    if (!price.ok) {
      context.addIssue({ code: "custom", message: price.message });
      return z.NEVER;
    }
    return price.value;
  }),
});

export type PitchFormValues = z.input<typeof pitchSchema>;
export type PitchFormData = z.output<typeof pitchSchema>;
