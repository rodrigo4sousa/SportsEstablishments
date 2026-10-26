"use client";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

type PeriodFilterProps = {
  /** Prefix of the field ids, so two filters on one page do not share them. */
  idPrefix: string;
  /** What is in the URL: null means "the default" (the last 30 days). */
  from: string | null;
  to: string | null;
  /** What to show in an empty field: the default period the page is using. */
  defaultFrom: string;
  defaultTo: string;
  onChange: (range: { from: string | null; to: string | null }) => void;
};

/**
 * A period of Lisbon dates, both included (US14, US25, US27, US28): «De», «Até» and a button back to the last 30 days. The fields
 * show the period in use; clearing one goes back to the default for that end.
 */
export function PeriodFilter({ idPrefix, from, to, defaultFrom, defaultTo, onChange }: PeriodFilterProps) {
  return (
    <div role="group" aria-label="Período" className="flex flex-wrap items-end gap-4">
      <div className="flex flex-col gap-1">
        <Label htmlFor={`${idPrefix}-from`}>De</Label>
        <Input
          id={`${idPrefix}-from`}
          type="date"
          value={from ?? defaultFrom}
          onChange={(event) => onChange({ from: event.target.value || null, to })}
        />
      </div>
      <div className="flex flex-col gap-1">
        <Label htmlFor={`${idPrefix}-to`}>Até</Label>
        <Input id={`${idPrefix}-to`} type="date" value={to ?? defaultTo} onChange={(event) => onChange({ from, to: event.target.value || null })} />
      </div>
      <Button type="button" variant="outline" disabled={from === null && to === null} onClick={() => onChange({ from: null, to: null })}>
        Últimos 30 dias
      </Button>
    </div>
  );
}
