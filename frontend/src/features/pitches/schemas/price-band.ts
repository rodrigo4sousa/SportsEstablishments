import { z } from "zod";
import { parsePrice } from "../price";
import { TIME_STEP_MINUTES, WEEKDAYS } from "../schedule";

/**
 * US10 price band rules, for immediate feedback only: the API validates again and has the last word (the 30-minute
 * step and the limits are provisional). Messages match the API's PriceBandRequestValidator.
 */
export const PRICE_BAND_MESSAGES = {
  days: "Escolhe pelo menos um dia da semana.",
  startRequired: "Indica a hora de início da faixa.",
  endRequired: "Indica a hora de fim da faixa.",
  invalidTime: "Indica a hora no formato HH:mm, por exemplo 18:00.",
  offStep: "As horas vão de meia em meia hora, por exemplo 18:00 ou 18:30.",
  endNotAfterStart:
    "O fim tem de ser depois do início. O fim mais tarde é 00:00 (meia-noite); faixas que passam da meia-noite não são aceites.",
} as const;

const TIME = /^([01]\d|2[0-3]):([0-5]\d)$/;

const DAY_KEYS = WEEKDAYS.map((day) => day.key);

export const priceBandSchema = z
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
      context.addIssue({ code: "custom", path: ["price"], message: price.message });
    }

    if (values.days.length === 0) {
      context.addIssue({ code: "custom", path: ["days"], message: PRICE_BAND_MESSAGES.days });
    }

    const minutes: Record<"start" | "end", number | null> = { start: null, end: null };
    for (const field of ["start", "end"] as const) {
      const value = values[field];
      if (value === "") {
        context.addIssue({ code: "custom", path: [field], message: field === "start" ? PRICE_BAND_MESSAGES.startRequired : PRICE_BAND_MESSAGES.endRequired });
        continue;
      }
      const match = TIME.exec(value);
      if (!match) {
        context.addIssue({ code: "custom", path: [field], message: PRICE_BAND_MESSAGES.invalidTime });
      } else if (Number(match[2]) % TIME_STEP_MINUTES !== 0) {
        context.addIssue({ code: "custom", path: [field], message: PRICE_BAND_MESSAGES.offStep });
      } else {
        minutes[field] = Number(match[1]) * 60 + Number(match[2]);
      }
    }

    // 00:00 as the end is midnight at the end of the day.
    if (minutes.start !== null && minutes.end !== null && minutes.end !== 0 && minutes.end <= minutes.start) {
      context.addIssue({ code: "custom", path: ["end"], message: PRICE_BAND_MESSAGES.endNotAfterStart });
    }
  })
  // Only reached when every check passed: the price is a number from here on.
  .transform((values) => {
    const price = parsePrice(values.price);
    return { ...values, price: price.ok ? price.value : 0 };
  });

export type PriceBandFormValues = z.input<typeof priceBandSchema>;
export type PriceBandFormData = z.output<typeof priceBandSchema>;

/** The weekday keys the form may send, in week order. */
export const PRICE_BAND_DAY_KEYS: readonly string[] = DAY_KEYS;
