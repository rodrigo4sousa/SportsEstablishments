import { z } from "zod";
import type { Translator } from "@/i18n/translator";
import { parsePrice } from "../price";
import { TIME_STEP_MINUTES, WEEKDAYS } from "../schedule";
import { priceErrorMessage } from "./pitch";

/**
 * US10 price band rules, for immediate feedback only: the API validates again and has the last word (the 30-minute
 * step and the limits are provisional). The rules match the API's PriceBandRequestValidator; the messages are the catalogs'
 * (`pitches.bandValidation`, `price.errors`).
 */
const TIME = /^([01]\d|2[0-3]):([0-5]\d)$/;

const DAY_KEYS = WEEKDAYS.map((day) => day.key);

export function createPriceBandSchema(t: Translator<"pitches.bandValidation">, priceErrors: Translator<"price.errors">) {
  return (
    z
      .object({
        // A group of checkboxes gives an array, but a lone value or `false` when nothing (or one box) is checked.
        days: z.preprocess((value) => (Array.isArray(value) ? value : typeof value === "string" && value !== "" ? [value] : []), z.array(z.string())),
        start: z.string().trim(),
        end: z.string().trim(),
        price: z.string(),
      })
      .superRefine((values, context) => {
        // All the checks live here, so a bad price does not hide the other messages.
        const price = parsePrice(values.price);
        if (!price.ok) {
          context.addIssue({ code: "custom", path: ["price"], message: priceErrorMessage(priceErrors, price.error) });
        }

        if (values.days.length === 0) {
          context.addIssue({ code: "custom", path: ["days"], message: t("days") });
        }

        const minutes: Record<"start" | "end", number | null> = { start: null, end: null };
        for (const field of ["start", "end"] as const) {
          const value = values[field];
          if (value === "") {
            context.addIssue({ code: "custom", path: [field], message: field === "start" ? t("startRequired") : t("endRequired") });
            continue;
          }
          const match = TIME.exec(value);
          if (!match) {
            context.addIssue({ code: "custom", path: [field], message: t("invalidTime") });
          } else if (Number(match[2]) % TIME_STEP_MINUTES !== 0) {
            context.addIssue({ code: "custom", path: [field], message: t("offStep") });
          } else {
            minutes[field] = Number(match[1]) * 60 + Number(match[2]);
          }
        }

        // 00:00 as the end is midnight at the end of the day.
        if (minutes.start !== null && minutes.end !== null && minutes.end !== 0 && minutes.end <= minutes.start) {
          context.addIssue({ code: "custom", path: ["end"], message: t("endNotAfterStart") });
        }
      })
      // Only reached when every check passed: the price is a number from here on.
      .transform((values) => {
        const price = parsePrice(values.price);
        return { ...values, price: price.ok ? price.value : 0 };
      })
  );
}

export type PriceBandFormValues = z.input<ReturnType<typeof createPriceBandSchema>>;
export type PriceBandFormData = z.output<ReturnType<typeof createPriceBandSchema>>;

/** The weekday keys the form may send, in week order. */
export const PRICE_BAND_DAY_KEYS: readonly string[] = DAY_KEYS;
