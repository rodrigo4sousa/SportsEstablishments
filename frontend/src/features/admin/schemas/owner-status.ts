import { z } from "zod";
import type { Translator } from "@/i18n/translator";

/**
 * US29 rules for the reason given when deactivating or reactivating an owner, for immediate feedback only: the API validates
 * again and has the last word. The rules match the API's `OwnerStatusRequestValidator`; the messages are the catalogs'.
 */
export const OWNER_STATUS_REASON_MAX_LENGTH = 500;

export function createOwnerStatusSchema(t: Translator<"admin.validation">) {
  return z.object({
    reason: z
      .string()
      .trim()
      .min(1, t("reasonRequired"))
      .max(OWNER_STATUS_REASON_MAX_LENGTH, t("reasonTooLong", { max: OWNER_STATUS_REASON_MAX_LENGTH })),
  });
}

export type OwnerStatusValues = z.input<ReturnType<typeof createOwnerStatusSchema>>;
export type OwnerStatusData = z.output<ReturnType<typeof createOwnerStatusSchema>>;
