import { z } from "zod";
import type { Translator } from "@/i18n/translator";
import { lisbonNowLocal } from "@/lib/datetime";
import { TIME_STEP_MINUTES } from "../schedule";

/**
 * US09 block rules, for immediate feedback only: the API validates again and has the last word (the 30-minute step,
 * the 90-day limit and the 200-character reason are provisional, see US09). The rules match the API's BlockCheckValidator;
 * the messages are the catalogs' (`pitches.blockValidation`).
 */
export const BLOCK_REASON_MAX_LENGTH = 200;

const DATE = /^\d{4}-\d{2}-\d{2}$/;
const TIME = /^([01]\d|2[0-3]):([0-5]\d)$/;

type Part = "start" | "end";

export function createBlockSchema(t: Translator<"pitches.blockValidation">, now: () => Date = () => new Date()) {
  return z
    .object({
      startDate: z.string().trim(),
      startTime: z.string().trim(),
      endDate: z.string().trim(),
      endTime: z.string().trim(),
      reason: z.string().trim().max(BLOCK_REASON_MAX_LENGTH, t("reason", { max: BLOCK_REASON_MAX_LENGTH })),
    })
    .superRefine((values, context) => {
      const ok: Record<Part, boolean> = { start: false, end: false };
      for (const part of ["start", "end"] as const) {
        const date = values[`${part}Date`];
        const time = values[`${part}Time`];
        const required = part === "start" ? t("startRequired") : t("endRequired");
        if (date === "") {
          context.addIssue({ code: "custom", path: [`${part}Date`], message: required });
        } else if (!DATE.test(date) || Number.isNaN(Date.parse(`${date}T00:00:00Z`))) {
          context.addIssue({ code: "custom", path: [`${part}Date`], message: t("invalidDate") });
        }
        if (time === "") {
          context.addIssue({
            code: "custom",
            path: [`${part}Time`],
            message: date === "" ? required : part === "start" ? t("startTimeRequired") : t("endTimeRequired"),
          });
        } else if (!TIME.test(time)) {
          context.addIssue({ code: "custom", path: [`${part}Time`], message: t("invalidTime") });
        } else if (Number(time.slice(3)) % TIME_STEP_MINUTES !== 0) {
          context.addIssue({ code: "custom", path: [`${part}Time`], message: t("offStep") });
        } else {
          ok[part] = date !== "" && DATE.test(date) && !Number.isNaN(Date.parse(`${date}T00:00:00Z`));
        }
      }

      const start = `${values.startDate}T${values.startTime}`;
      const end = `${values.endDate}T${values.endTime}`;
      // Local "YYYY-MM-DDTHH:mm" strings compare in time order. (Around the October clock change the API has the last word.)
      if (ok.start && start < lisbonNowLocal(now())) {
        context.addIssue({ code: "custom", path: ["startDate"], message: t("past") });
      }
      if (ok.start && ok.end && end <= start) {
        context.addIssue({ code: "custom", path: ["endDate"], message: t("endNotAfterStart") });
      }
    });
}

export type BlockFormValues = z.input<ReturnType<typeof createBlockSchema>>;
export type BlockFormData = z.output<ReturnType<typeof createBlockSchema>>;
