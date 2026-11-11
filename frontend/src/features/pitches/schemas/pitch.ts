import { z } from "zod";
import type { Translator } from "@/i18n/translator";
import { PRICE_DECIMALS, PRICE_MAX, parsePrice, type PriceError } from "../price";
import { isSportCode } from "../sports";

/**
 * US07 pitch rules, for immediate feedback only: the API validates again and has the last word.
 * Limits match the API's PitchRequestValidator (the limits are provisional, see US07); the messages are the catalogs'.
 */
export const PITCH_NAME_MAX_LENGTH = 100;

export { PRICE_DECIMALS };

/** A typed price ("12,50") as a number of euros, or the catalog's message for why it is not one. */
export function priceField(price: Translator<"price.errors">) {
  return z.string().transform((text, context) => {
    const parsed = parsePrice(text);
    if (!parsed.ok) {
      context.addIssue({ code: "custom", message: priceErrorMessage(price, parsed.error) });
      return z.NEVER;
    }
    return parsed.value;
  });
}

export function priceErrorMessage(price: Translator<"price.errors">, error: PriceError): string {
  return error === "tooHigh" ? price("tooHigh", { max: PRICE_MAX }) : price(error);
}

export function createPitchSchema(t: Translator<"pitches.validation">, price: Translator<"price.errors">) {
  return z.object({
    name: z
      .string()
      .trim()
      .min(1, t("nameRequired"))
      .max(PITCH_NAME_MAX_LENGTH, t("nameMax", { max: PITCH_NAME_MAX_LENGTH })),
    sport: z.string().min(1, t("sportRequired")).refine(isSportCode, t("sportInvalid")),
    // A text field in the form (what was typed, "12,50"); a number of euros once valid.
    pricePerSlot: priceField(price),
  });
}

export type PitchFormValues = z.input<ReturnType<typeof createPitchSchema>>;
export type PitchFormData = z.output<ReturnType<typeof createPitchSchema>>;
