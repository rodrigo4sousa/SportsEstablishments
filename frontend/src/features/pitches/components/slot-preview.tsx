"use client";

import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api/client";
import { UNKNOWN_ERROR, useApiErrors } from "@/lib/api/error-messages";
import { useFormats } from "@/i18n/formats";
import { isDateKey } from "@/lib/datetime";
import { fetchSlots, slotsQueryKey } from "../api";
import { BOOKING_DAYS_AHEAD, previewDateRange } from "../schedule";

/**
 * US08: the slots a pitch's hours generate on a date, today to 30 days ahead, in Lisbon time. A read-only preview:
 * a slot taken by a confirmed booking is marked "Reservado" and one that overlaps a block "Bloqueado" (it can be both).
 * The repeated October hour is labelled "(verão)" and "(inverno)". Each slot shows the price a booking costs now (US10).
 */
export function SlotPreview({ clubId, pitchId }: { clubId: string; pitchId: string }) {
  const t = useTranslations("pitches.preview");
  const common = useTranslations("common");
  const dates = useTranslations("dates");
  const time = useTranslations("time");
  const apiErrors = useApiErrors();
  const formats = useFormats();
  const [range] = useState(() => previewDateRange());
  const [date, setDate] = useState(range.min);
  const inWindow = isDateKey(date) && date >= range.min && date <= range.max;

  const slots = useQuery({
    queryKey: slotsQueryKey(clubId, pitchId, date),
    queryFn: () => fetchSlots(clubId, pitchId, date),
    enabled: inWindow,
  });

  const inputId = `preview-date-${pitchId}`;
  let result;
  if (!inWindow) {
    result = (
      <p id={`${inputId}-error`} role="alert" className="text-sm text-destructive">
        {date === "" ? dates("chooseDate") : dates("outOfWindow", { days: BOOKING_DAYS_AHEAD })}
      </p>
    );
  } else if (slots.isPending) {
    result = (
      <p role="status" className="text-muted-foreground">
        {t("loading")}
      </p>
    );
  } else if (slots.isError) {
    const code = slots.error instanceof ApiError ? slots.error.code : UNKNOWN_ERROR;
    result = (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{apiErrors.message(code)}</p>
        <Button type="button" variant="outline" onClick={() => void slots.refetch()}>
          {common("retry")}
        </Button>
      </div>
    );
  } else if (slots.data.slots.length === 0) {
    result = <p className="text-muted-foreground">{t("empty")}</p>;
  } else {
    result = (
      <>
        <p className="text-sm text-muted-foreground">
          {t("summary", { date: formats.longDate(slots.data.slots[0].startsAt), count: slots.data.slots.length, minutes: slots.data.slotDurationMinutes })}
        </p>
        <ul aria-label={t("list")} className="flex flex-wrap gap-2">
          {slots.data.slots.map((slot) => (
            <li
              key={slot.startsAt}
              data-blocked={slot.blocked || undefined}
              data-booked={slot.booked || undefined}
              className={`rounded-md border px-3 py-1 text-sm tabular-nums ${slot.blocked || slot.booked ? "border-dashed bg-muted text-muted-foreground" : ""}`}
            >
              {formats.slotTime(slot.startsAt)} – {formats.slotTime(slot.endsAt)} · {formats.price(slot.price)}
              {slot.booked ? (
                <>
                  {" "}
                  <span className="ml-1 font-medium">{t("booked")}</span>
                </>
              ) : null}
              {slot.blocked ? (
                <>
                  {" "}
                  <span className="ml-1 font-medium">{t("blocked")}</span>
                </>
              ) : null}
            </li>
          ))}
        </ul>
      </>
    );
  }

  return (
    <section aria-label={t("title")} className="flex flex-col gap-3">
      <h4 className="font-medium">{t("title")}</h4>
      <div className="flex flex-col gap-2">
        <Label htmlFor={inputId}>{common("date")}</Label>
        <Input
          id={inputId}
          type="date"
          min={range.min}
          max={range.max}
          value={date}
          className="sm:max-w-48"
          aria-invalid={inWindow ? undefined : true}
          aria-describedby={inWindow ? undefined : `${inputId}-error`}
          onChange={(event) => setDate(event.target.value)}
        />
      </div>
      {result}
      <p className="text-sm text-muted-foreground">
        {t("note")} {time("lisbonTimeNote")}
      </p>
    </section>
  );
}
