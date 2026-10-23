import type { FieldValues, Path, UseFormReturn } from "react-hook-form";

/**
 * Puts the API's per-field messages (ProblemDetails `errors`, keyed by camelCase field name) on their fields and
 * focuses the first one. Returns false when none of them is a field of this form, so the caller can show a toast.
 */
export function showApiFieldErrors<T extends FieldValues>(
  form: Pick<UseFormReturn<T>, "setError">,
  fieldErrors: Readonly<Record<string, string[]>>,
  fields: ReadonlyArray<Path<T>>,
): boolean {
  const withErrors = fields.filter((field) => fieldErrors[field]?.length);
  withErrors.forEach((field, index) =>
    form.setError(field, { message: fieldErrors[field].join(" ") }, { shouldFocus: index === 0 }),
  );
  return withErrors.length > 0;
}
