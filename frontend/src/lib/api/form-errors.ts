import { useCallback } from "react";
import { useTranslations } from "next-intl";
import type { FieldValues, Path, UseFormReturn } from "react-hook-form";

/**
 * Marks the fields the API refused (ProblemDetails `errors`, keyed by camelCase field name) and focuses the first one. The API's own
 * words are never shown (T09: they are pt-PT free text): each field gets the catalog's message for it, from `message(field)`. Returns
 * false when none of them is a field of this form, so the caller can show a toast.
 */
export function showApiFieldErrors<T extends FieldValues>(
  form: Pick<UseFormReturn<T>, "setError">,
  fieldErrors: Readonly<Record<string, string[]>>,
  fields: ReadonlyArray<Path<T>>,
  message: (field: Path<T>) => string,
): boolean {
  const withErrors = fields.filter((field) => fieldErrors[field]?.length);
  withErrors.forEach((field, index) => form.setError(field, { message: message(field) }, { shouldFocus: index === 0 }));
  return withErrors.length > 0;
}

/** The scopes of `fieldErrors.*` that have their own words for some fields (e.g. a pitch's name must be unique in its club). */
type Scope = "pitch" | "club" | "block" | "priceBand" | "schedule" | "booking" | "account" | "period";

/**
 * The message for a field the API refused: the scope's own words for that field, else the shared words for a field of that name, else
 * the general «this value was not accepted». The rules each message names are the ones the API checks, so the form's own validation
 * (which mirrors them) has usually caught the mistake before the API sees it; these are for what only the API can know.
 */
export function useApiFieldMessage(scope?: Scope) {
  const t = useTranslations("fieldErrors");
  return useCallback(
    (field: string) => {
      // The last part of a nested key ("monday.closes" → "closes", "days.monday.opens" → "opens") names the rule.
      const leaf = field.split(".").at(-1) ?? field;
      for (const key of [scope ? `${scope}.${leaf}` : null, `shared.${leaf}`]) {
        if (key && t.has(key as never)) {
          return t(key as never);
        }
      }
      return t("invalid");
    },
    [scope, t],
  );
}
