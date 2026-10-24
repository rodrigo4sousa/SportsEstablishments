"use client";

import { useCallback, useState } from "react";
import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { PITCHES_PAGE_SIZE, deactivatePitch, fetchPitches, pitchListQueryKey, pitchesQueryKey, reactivatePitch, type Pitch } from "../api";
import { formatPrice } from "../price";
import { sportLabel } from "../sports";
import { BlocksSection } from "./blocks-section";
import { PitchForm } from "./pitch-form";
import { ScheduleEditor } from "./schedule-editor";
import { SlotPreview } from "./slot-preview";

/** What the form below the list is doing: nothing, adding a pitch, or editing this one. */
type Editing = { kind: "new" } | { kind: "edit"; pitch: Pitch } | null;

/**
 * US07: the pitches of one of the owner's clubs, with add, edit, deactivate and reactivate. Inactive pitches stay in
 * the list, marked, because they are never deleted.
 */
export function PitchSection({ clubId }: { clubId: string }) {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<Editing>(null);
  // The pitch whose hours or blocks are open below its row (one panel at a time).
  const [panel, setPanel] = useState<{ pitchId: string; kind: "schedule" | "blocks" } | null>(null);
  const togglePanel = (pitchId: string, kind: "schedule" | "blocks") =>
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
      toast.success(activate ? `«${saved.name}» foi reativado.` : `«${saved.name}» foi desativado.`);
    },
    onError: async (error) => {
      toast.error(errorMessage(error instanceof ApiError ? error.code : "unknown_error"));
      if (error instanceof ApiError && error.code === "pitch_not_found") {
        // It is gone: close its form, if open, and show the list as it is now.
        setEditing(null);
        setPanel(null);
        await queryClient.invalidateQueries({ queryKey: pitchesQueryKey(clubId), refetchType: "all" });
      }
    },
  });

  const closePanel = useCallback(() => setPanel(null), []);

  const heading = (
    <div className="flex items-center justify-between gap-4">
      <h2 id="pitches-heading" className="text-xl font-semibold tracking-tight">
        Campos
      </h2>
      {editing === null ? (
        <Button type="button" onClick={() => setEditing({ kind: "new" })}>
          Adicionar campo
        </Button>
      ) : null}
    </div>
  );

  let list;
  if (pitches.isPending) {
    list = (
      <p role="status" className="text-muted-foreground">
        A carregar os campos…
      </p>
    );
  } else if (pitches.isError) {
    const code = pitches.error instanceof ApiError ? pitches.error.code : "unknown_error";
    list = (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{errorMessage(code)}</p>
        <Button type="button" variant="outline" onClick={() => void pitches.refetch()}>
          Tentar novamente
        </Button>
      </div>
    );
  } else if (pitches.data.totalCount === 0) {
    list = <p className="text-muted-foreground">Este clube ainda não tem campos. Adiciona o primeiro para o poderes configurar.</p>;
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
                    <span className="rounded-full border px-2 py-0.5 text-xs font-normal">Inativo</span>
                  ) : null}
                </span>
                <span className="text-sm text-muted-foreground">
                  {sportLabel(pitch.sport)} · {formatPrice(pitch.pricePerSlot)} por slot
                </span>
              </div>
              <div className="flex gap-2">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  aria-expanded={panel?.pitchId === pitch.id && panel.kind === "schedule"}
                  aria-label={`Horário de ${pitch.name}`}
                  onClick={() => togglePanel(pitch.id, "schedule")}
                >
                  Horário
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  aria-expanded={panel?.pitchId === pitch.id && panel.kind === "blocks"}
                  aria-label={`Bloqueios de ${pitch.name}`}
                  onClick={() => togglePanel(pitch.id, "blocks")}
                >
                  Bloqueios
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  aria-label={`Editar ${pitch.name}`}
                  onClick={() => setEditing({ kind: "edit", pitch })}
                >
                  Editar
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={toggle.isPending}
                  aria-label={`${pitch.status === "active" ? "Desativar" : "Reativar"} ${pitch.name}`}
                  onClick={() => toggle.mutate({ pitch, activate: pitch.status === "inactive" })}
                >
                  {pitch.status === "active" ? "Desativar" : "Reativar"}
                </Button>
              </div>
              </div>
              {panel?.pitchId === pitch.id && panel.kind === "schedule" ? (
                <div className="flex flex-col gap-6 border-t pt-4">
                  <h3 className="font-medium">Horário de {pitch.name}</h3>
                  <ScheduleEditor clubId={clubId} pitchId={pitch.id} onGone={closePanel} />
                  <SlotPreview clubId={clubId} pitchId={pitch.id} />
                </div>
              ) : null}
              {panel?.pitchId === pitch.id && panel.kind === "blocks" ? (
                <div className="flex flex-col gap-6 border-t pt-4">
                  <h3 className="font-medium">Bloqueios de {pitch.name}</h3>
                  <BlocksSection clubId={clubId} pitchId={pitch.id} onGone={closePanel} />
                </div>
              ) : null}
            </li>
          ))}
        </ul>

        {pageCount > 1 ? (
          <nav aria-label="Páginas dos campos" className="flex items-center justify-between gap-4">
            <Button type="button" variant="outline" disabled={page <= 1} onClick={() => setPage(page - 1)}>
              Anterior
            </Button>
            <p className="text-sm text-muted-foreground">
              Página {page} de {pageCount}
            </p>
            <Button type="button" variant="outline" disabled={page >= pageCount} onClick={() => setPage(page + 1)}>
              Seguinte
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
