"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api/client";
import { UNKNOWN_ERROR, useApiErrors } from "@/lib/api/error-messages";
import { deletePriceBand, fetchPriceBands, pitchesQueryKey, priceBandsQueryKey, slotsQueryKey, type PriceBand } from "../api";
import { useFormats } from "@/i18n/formats";
import { WEEKDAYS, type WeekdayKey } from "../schedule";
import { PriceBandForm } from "./price-band-form";

const MAX_BANDS = 20;

/** "Segunda-feira, Sexta-feira" for the API's names, Monday first; `label` gives a day's name in the page's language. */
export function weekdaysLabel(days: readonly string[], label: (key: WeekdayKey) => string): string {
  return WEEKDAYS.filter((day) => days.includes(day.key))
    .map((day) => label(day.key))
    .join(", ");
}

/** "18:00–22:00", with the midnight end written as 24:00 so it does not read like an empty range. */
export function hoursLabel(band: Pick<PriceBand, "start" | "end">): string {
  return `${band.start}–${band.end === "00:00" ? "24:00" : band.end}`;
}

/** What is open below the list: nothing, a form for a new band, or the form of this one. */
type Editing = { kind: "new" } | { kind: "edit"; bandId: string } | null;

/**
 * US10: the price bands of a pitch: list (weekdays, hours, price), add, edit, and delete after a confirmation. A slot
 * outside every band costs the pitch's base price; the preview shows each slot's price.
 */
export function PriceBandsSection({ clubId, pitchId, onGone }: { clubId: string; pitchId: string; onGone: () => void }) {
  const t = useTranslations("pitches.bands");
  const common = useTranslations("common");
  const weekdays = useTranslations("weekdays");
  const apiErrors = useApiErrors();
  const formats = useFormats();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<Editing>(null);
  const [confirming, setConfirming] = useState<string | null>(null);
  const bands = useQuery({ queryKey: priceBandsQueryKey(clubId, pitchId), queryFn: () => fetchPriceBands(clubId, pitchId) });

  const refresh = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: priceBandsQueryKey(clubId, pitchId), refetchType: "all" }),
      queryClient.invalidateQueries({ queryKey: slotsQueryKey(clubId, pitchId), refetchType: "all" }),
    ]);

  const remove = useMutation({
    mutationFn: (band: PriceBand) => deletePriceBand(clubId, pitchId, band.id),
    onSuccess: async () => {
      setConfirming(null);
      await refresh();
      toast.success(t("removed"));
    },
    onError: async (error) => {
      const code = error instanceof ApiError ? error.code : UNKNOWN_ERROR;
      toast.error(apiErrors.message(code));
      setConfirming(null);
      if (code === "price_band_not_found") {
        await refresh();
      } else if (code === "pitch_not_found") {
        await queryClient.invalidateQueries({ queryKey: pitchesQueryKey(clubId), refetchType: "all" });
        onGone();
      }
    },
  });

  let list;
  if (bands.isPending) {
    list = (
      <p role="status" className="text-muted-foreground">
        {t("loading")}
      </p>
    );
  } else if (bands.isError) {
    const code = bands.error instanceof ApiError ? bands.error.code : UNKNOWN_ERROR;
    list = (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{apiErrors.message(code)}</p>
        <Button type="button" variant="outline" onClick={() => void bands.refetch()}>
          {common("retry")}
        </Button>
      </div>
    );
  } else if (bands.data.length === 0) {
    list = <p className="text-muted-foreground">{t("empty")}</p>;
  } else {
    list = (
      <ul aria-label={t("list")} className="flex flex-col divide-y rounded-md border">
        {bands.data.map((band) => (
          <li key={band.id} className="flex flex-col gap-2 px-4 py-3">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="flex flex-col gap-1">
                <span className="font-medium tabular-nums">
                  {hoursLabel(band)} · {formats.price(band.price)}
                </span>
                <span className="text-sm text-muted-foreground">{weekdaysLabel(band.days, (key) => weekdays(key))}</span>
              </div>
              {confirming === band.id ? null : (
                <div className="flex gap-2">
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    aria-label={t("editOf", { hours: hoursLabel(band) })}
                    onClick={() => setEditing({ kind: "edit", bandId: band.id })}
                  >
                    {common("edit")}
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    aria-label={t("removeOf", { hours: hoursLabel(band) })}
                    onClick={() => setConfirming(band.id)}
                  >
                    {common("remove")}
                  </Button>
                </div>
              )}
            </div>
            {editing?.kind === "edit" && editing.bandId === band.id ? (
              <PriceBandForm clubId={clubId} pitchId={pitchId} band={band} onDone={() => setEditing(null)} />
            ) : null}
            {confirming === band.id ? (
              <div role="group" aria-label={t("confirmGroup")} className="flex flex-wrap items-center gap-3 rounded-md bg-muted px-3 py-2">
                <p className="text-sm">{t("confirmQuestion")}</p>
                <Button type="button" size="sm" variant="destructive" disabled={remove.isPending} onClick={() => remove.mutate(band)}>
                  {remove.isPending ? common("removing") : common("confirmRemoval")}
                </Button>
                <Button type="button" size="sm" variant="ghost" disabled={remove.isPending} onClick={() => setConfirming(null)}>
                  {common("cancel")}
                </Button>
              </div>
            ) : null}
          </li>
        ))}
      </ul>
    );
  }

  const full = bands.data !== undefined && bands.data.length >= MAX_BANDS;

  return (
    <section aria-label={t("title")} className="flex flex-col gap-4">
      <div className="flex items-center justify-between gap-4">
        <h4 className="font-medium">{t("title")}</h4>
        {editing?.kind === "new" ? null : (
          <Button type="button" size="sm" disabled={full} onClick={() => setEditing({ kind: "new" })}>
            {t("add")}
          </Button>
        )}
      </div>
      {full ? <p className="text-sm text-muted-foreground">{t("full", { max: MAX_BANDS })}</p> : null}
      {editing?.kind === "new" ? <PriceBandForm clubId={clubId} pitchId={pitchId} onDone={() => setEditing(null)} /> : null}
      {list}
    </section>
  );
}
