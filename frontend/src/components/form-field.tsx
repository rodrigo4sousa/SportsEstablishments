import type { ReactNode } from "react";
import { Label } from "@/components/ui/label";

type FormFieldProps = {
  /** The id of the input inside; the hint and the error get `${id}-hint` and `${id}-error` for `aria-describedby`. */
  id: string;
  label: string;
  hint?: string;
  error?: string;
  children: ReactNode;
};

/** A labelled input with an optional hint and, once validation fails, one error message for the field. */
export function FormField({ id, label, hint, error, children }: FormFieldProps) {
  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id}>{label}</Label>
      {children}
      {hint ? (
        <p id={`${id}-hint`} className="text-sm text-muted-foreground">
          {hint}
        </p>
      ) : null}
      {error ? (
        <p id={`${id}-error`} className="text-sm text-destructive">
          {error}
        </p>
      ) : null}
    </div>
  );
}
