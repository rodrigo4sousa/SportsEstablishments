"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { useTranslations } from "next-intl";
import { LoadingRegion } from "@/components/loading-region";
import { EmptyState } from "@/components/empty-state";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api/client";
import { UNKNOWN_ERROR, useApiErrors } from "@/lib/api/error-messages";
import { useRestoreFocus } from "@/lib/focus";
import { PITCHES_PAGE_SIZE, deactivatePitch, fetchPitches, pitchListQueryKey, pitchesQueryKey, reactivatePitch, type Pitch } from "../api";
import { useFormats } from "@/i18n/formats";
import { useSportLabel } from "../sports";
import { BlocksSection } from "./blocks-section";
import { PitchForm } from "./pitch-form";
import { PriceBandsSection } from "./price-bands-section";
import { ScheduleEditor } from "./schedule-editor";
import { SlotPreview } from "./slot-preview";

type PanelKind = "schedule" | "blocks" | "prices";

/** What the form below the list is doing: nothing, adding a pitch, or editing this one. */
type Editing = { kind: "new" } | { kind: "edit"; pitch: Pitch } | null;

/**
 * US07: the pitches of one of the owner's clubs, with add, edit, deactivate and reactivate. Inactive pitches stay in
 * the list, marked, because they are never deleted.
 */
export function PitchSection({ clubId }: { clubId: string }) {
  const t = useTranslations("pitches.section");
  const common = useTranslations("common");
  const apiErrors = useApiErrors();
  const formats = useFormats();
  const sportLabel = useSportLabel();
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<Editing>(null);
  // The pitch whose hours or blocks are open below its row (one panel at a time).
  const [panel, setPanel] = useState<{ pitchId: string; kind: PanelKind } | null>(null);
  const togglePanel = (pitchId: string, kind: PanelKind) =>
    setPanel(panel?.pitchId === pitchId && panel.kind === kind ? null : { pitchId, kind });
  const pitches = useQuery({
    queryKey: pitchListQueryKey(clubId, page),
    queryFn: () => fetchPitches(clubId, page),
    placeholderData: keepPreviousData,
  });

  const toggle = useMutation({
    mutationFn: ({ pitch, activate }: { pitch: Pitch; activate: boolean }) =>
      activate ? reactivatePitch(clubId, pitch.id) : deactivatePitch(clubId, pitch.id),
    onSuccess: async (saved, { activate }) => {
      await queryClient.invalidateQueries({ queryKey: pitchesQueryKey(clubId), refetchType: "all" });
      toast.success(activate ? t("reactivated", { name: saved.name }) : t("deactivated", { name: saved.name }));
    },
    onError: async (error) => {
      toast.error(apiErrors.message(error instanceof ApiError ? error.code : UNKNOWN_ERROR));
      if (error instanceof ApiError && error.code === "pitch_not_found") {
        // It is gone: close its form, if open, and show the list as it is now.
        setEditing(null);
        setPanel(null);
        await queryClient.invalidateQueries({ queryKey: pitchesQueryKey(clubId), refetchType: "all" });
      }
    },
  });

  const closePanel = useCallback(() => setPanel(null), []);

  // UX-002: the form replaces «Adicionar campo» (or opens under the list for «Editar»); when it closes, focus goes back to the control
  // that opened it, not to <body>.
  const addButton = useRestoreFocus<HTMLButtonElement>(editing?.kind === "new");
  const editedPitch = useRef<string | null>(null);
  useEffect(() => {
    if (editing?.kind === "edit") {
      editedPitch.current = editing.pitch.id;
    } else if (editing === null && editedPitch.current !== null) {
      const id = editedPitch.current;
      editedPitch.current = null;
      const active = document.activeElement;
      if (!active || active === document.body) {
        document.querySelector<HTMLElement>(`[data-edit-pitch="${id}"]`)?.focus();
      }
    }
  }, [editing]);

  const heading = (
    <div className="flex items-center justify-between gap-4">
      <h2 id="pitches-heading" className="text-xl font-semibold tracking-tight">
        {t("title")}
      </h2>
      {editing === null ? (
        <Button ref={addButton} type="button" size="cta" onClick={() => setEditing({ kind: "new" })}>
          {t("add")}
        </Button>
      ) : null}
    </div>
  );

  let list;
  if (pitches.isPending) {
    list = (
      <LoadingRegion label={t("loading")} className="flex flex-col divide-y rounded-md border">
        {[0, 1].map((row) => (
          <div key={row} className="flex flex-col gap-3 px-4 py-3">
            <Skeleton className="h-5 w-40" />
            <Skeleton className="h-4 w-56 max-w-full" />
            <Skeleton className="h-11 w-full sm:h-8 sm:w-96" />
          </div>
        ))}
      </LoadingRegion>
    );
  } else if (pitches.isError) {
    const code = pitches.error instanceof ApiError ? pitches.error.code : UNKNOWN_ERROR;
    list = (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{apiErrors.message(code)}</p>
        <Button type="button" variant="outline" onClick={() => void pitches.refetch()}>
          {common("retry")}
        </Button>
      </div>
    );
  } else if (pitches.data.totalCount === 0) {
    list = <EmptyState title={t("emptyTitle")} description={t("emptyDescription")} />;
  } else {
    const { items, totalCount } = pitches.data;
    const pageCount = Math.max(1, Math.ceil(totalCount / PITCHES_PAGE_SIZE));
    list = (
      <>
        <ul aria-labelledby="pitches-heading" className="flex flex-col divide-y rounded-md border">
          {items.map((pitch) => (
            <li key={pitch.id} className="flex flex-col gap-4 px-4 py-3" data-status={pitch.status}>
              <div className="flex flex-wrap items-center justify-between gap-3">
              <div className={pitch.status === "inactive" ? "flex flex-col gap-1 text-muted-foreground" : "flex flex-col gap-1"}>
                <span className="flex items-center gap-2 font-medium">
                  {pitch.name}
                  {pitch.status === "inactive" ? (
                    <span className="rounded-full border px-2 py-0.5 text-xs font-normal">{t("inactive")}</span>
                  ) : null}
                </span>
                <span className="text-sm text-muted-foreground">
                  {sportLabel(pitch.sport)} · {t("pricePerSlot", { price: formats.price(pitch.pricePerSlot) })}
                </span>
              </div>
              <div className="flex flex-wrap gap-2">
                <Button
                  type="button"
                  variant="outline"
                  size="cta"
                  aria-expanded={panel?.pitchId === pitch.id && panel.kind === "schedule"}
                  aria-label={t("scheduleOf", { pitch: pitch.name })}
                  onClick={() => togglePanel(pitch.id, "schedule")}
                >
                  {t("schedule")}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="cta"
                  aria-expanded={panel?.pitchId === pitch.id && panel.kind === "blocks"}
                  aria-label={t("blocksOf", { pitch: pitch.name })}
                  onClick={() => togglePanel(pitch.id, "blocks")}
                >
                  {t("blocks")}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="cta"
                  aria-expanded={panel?.pitchId === pitch.id && panel.kind === "prices"}
                  aria-label={t("pricesOf", { pitch: pitch.name })}
                  onClick={() => togglePanel(pitch.id, "prices")}
                >
                  {t("prices")}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="cta"
                  aria-label={t("editOf", { pitch: pitch.name })}
                  data-edit-pitch={pitch.id}
                  onClick={() => setEditing({ kind: "edit", pitch })}
                >
                  {common("edit")}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="cta"
                  disabled={toggle.isPending}
                  className={pitch.status === "active" ? "border-destructive/40 text-destructive hover:text-destructive" : undefined}
                  aria-label={pitch.status === "active" ? t("deactivateOf", { pitch: pitch.name }) : t("reactivateOf", { pitch: pitch.name })}
                  onClick={() => toggle.mutate({ pitch, activate: pitch.status === "inactive" })}
                >
                  {pitch.status === "active" ? t("deactivate") : t("reactivate")}
                </Button>
              </div>
              </div>
              {panel?.pitchId === pitch.id && panel.kind === "schedule" ? (
                <div className="flex flex-col gap-6 border-t pt-4">
                  <h3 className="font-medium">{t("scheduleOf", { pitch: pitch.name })}</h3>
                  <ScheduleEditor clubId={clubId} pitchId={pitch.id} onGone={closePanel} />
                  <SlotPreview clubId={clubId} pitchId={pitch.id} />
                </div>
              ) : null}
              {panel?.pitchId === pitch.id && panel.kind === "blocks" ? (
                <div className="flex flex-col gap-6 border-t pt-4">
                  <h3 className="font-medium">{t("blocksOf", { pitch: pitch.name })}</h3>
                  <BlocksSection clubId={clubId} pitchId={pitch.id} onGone={closePanel} />
                </div>
              ) : null}
              {panel?.pitchId === pitch.id && panel.kind === "prices" ? (
                <div className="flex flex-col gap-6 border-t pt-4">
                  <h3 className="font-medium">{t("pricesOf", { pitch: pitch.name })}</h3>
                  <PriceBandsSection clubId={clubId} pitchId={pitch.id} onGone={closePanel} />
                </div>
              ) : null}
            </li>
          ))}
        </ul>

        {pageCount > 1 ? (
          <nav aria-label={t("pages")} className="flex items-center justify-between gap-4">
            <Button type="button" variant="outline" disabled={page <= 1} onClick={() => setPage(page - 1)}>
              {common("previous")}
            </Button>
            <p className="text-sm text-muted-foreground">{common("pageOf", { page, pages: pageCount })}</p>
            <Button type="button" variant="outline" disabled={page >= pageCount} onClick={() => setPage(page + 1)}>
              {common("next")}
            </Button>
          </nav>
        ) : null}
      </>
    );
  }

  return (
    <section aria-labelledby="pitches-heading" className="flex flex-col gap-4">
      {heading}
      {editing ? (
        <PitchForm
          key={editing.kind === "edit" ? editing.pitch.id : "new"}
          clubId={clubId}
          pitch={editing.kind === "edit" ? editing.pitch : undefined}
          onDone={() => setEditing(null)}
        />
      ) : null}
      {list}
    </section>
  );
}
