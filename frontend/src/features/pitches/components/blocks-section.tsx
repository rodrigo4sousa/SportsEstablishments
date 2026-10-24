"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { formatLisbonDate, formatLisbonSlotTime } from "@/lib/datetime";
import { BLOCKS_PAGE_SIZE, blockListQueryKey, blocksQueryKey, deleteBlock, fetchBlocks, pitchesQueryKey, slotsQueryKey, type Block } from "../api";
import { BlockForm } from "./block-form";

/** "25/10/2026 09:00", or "25/10/2026 01:00 (verão)" inside the repeated October hour. Lisbon time, always. */
const when = (instant: string) => `${formatLisbonDate(instant)} ${formatLisbonSlotTime(instant)}`;

/**
 * US09: a pitch's ongoing and future blocks, a form to add one, and removal of those that have not started (after a
 * confirmation). A block that has started stays, as history.
 */
export function BlocksSection({ clubId, pitchId, onGone }: { clubId: string; pitchId: string; onGone: () => void }) {
  const queryClient = useQueryClient();
  const [page, setPage] = useState(1);
  const [adding, setAdding] = useState(false);
  const [confirming, setConfirming] = useState<string | null>(null);
  const blocks = useQuery({
    queryKey: blockListQueryKey(clubId, pitchId, page),
    queryFn: () => fetchBlocks(clubId, pitchId, page),
    placeholderData: keepPreviousData,
  });

  const refresh = () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: blocksQueryKey(clubId, pitchId), refetchType: "all" }),
      queryClient.invalidateQueries({ queryKey: slotsQueryKey(clubId, pitchId), refetchType: "all" }),
    ]);

  const remove = useMutation({
    mutationFn: (block: Block) => deleteBlock(clubId, pitchId, block.id),
    onSuccess: async () => {
      setConfirming(null);
      await refresh();
      toast.success("Bloqueio removido.");
    },
    onError: async (error) => {
      const code = error instanceof ApiError ? error.code : "unknown_error";
      toast.error(errorMessage(code));
      setConfirming(null);
      if (code === "block_already_started" || code === "block_not_found") {
        // It started (or was removed) while the owner was looking: show the list as it is now.
        await refresh();
      } else if (code === "pitch_not_found") {
        await queryClient.invalidateQueries({ queryKey: pitchesQueryKey(clubId), refetchType: "all" });
        onGone();
      }
    },
  });

  let list;
  if (blocks.isPending) {
    list = (
      <p role="status" className="text-muted-foreground">
        A carregar os bloqueios…
      </p>
    );
  } else if (blocks.isError) {
    const code = blocks.error instanceof ApiError ? blocks.error.code : "unknown_error";
    list = (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{errorMessage(code)}</p>
        <Button type="button" variant="outline" onClick={() => void blocks.refetch()}>
          Tentar novamente
        </Button>
      </div>
    );
  } else if (blocks.data.totalCount === 0) {
    list = <p className="text-muted-foreground">Sem bloqueios em curso nem futuros.</p>;
  } else {
    const { items, totalCount } = blocks.data;
    const pageCount = Math.max(1, Math.ceil(totalCount / BLOCKS_PAGE_SIZE));
    // "Now" is when the list was fetched, which keeps rendering pure; the API still refuses a block that has started.
    const now = blocks.dataUpdatedAt;
    list = (
      <>
        <ul aria-label="Bloqueios do campo" className="flex flex-col divide-y rounded-md border">
          {items.map((block) => {
            const started = new Date(block.startsAt).getTime() <= now;
            return (
              <li key={block.id} className="flex flex-col gap-2 px-4 py-3">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div className="flex flex-col gap-1">
                    <span className="font-medium tabular-nums">
                      {when(block.startsAt)} – {when(block.endsAt)}
                    </span>
                    <span className="text-sm text-muted-foreground">
                      {block.reason ?? "Sem motivo"}
                      {started ? " · Em curso" : ""}
                    </span>
                  </div>
                  {started ? null : confirming === block.id ? null : (
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      aria-label={`Remover bloqueio de ${when(block.startsAt)}`}
                      onClick={() => setConfirming(block.id)}
                    >
                      Remover
                    </Button>
                  )}
                </div>
                {confirming === block.id ? (
                  <div role="group" aria-label="Confirmar remoção" className="flex flex-wrap items-center gap-3 rounded-md bg-muted px-3 py-2">
                    <p className="text-sm">Remover este bloqueio? Os slots ficam livres outra vez.</p>
                    <Button type="button" size="sm" variant="destructive" disabled={remove.isPending} onClick={() => remove.mutate(block)}>
                      {remove.isPending ? "A remover…" : "Confirmar remoção"}
                    </Button>
                    <Button type="button" size="sm" variant="ghost" disabled={remove.isPending} onClick={() => setConfirming(null)}>
                      Cancelar
                    </Button>
                  </div>
                ) : null}
              </li>
            );
          })}
        </ul>

        {pageCount > 1 ? (
          <nav aria-label="Páginas dos bloqueios" className="flex items-center justify-between gap-4">
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
    <section aria-label="Bloqueios" className="flex flex-col gap-4">
      <div className="flex items-center justify-between gap-4">
        <h4 className="font-medium">Bloqueios</h4>
        {adding ? null : (
          <Button type="button" size="sm" onClick={() => setAdding(true)}>
            Novo bloqueio
          </Button>
        )}
      </div>
      {adding ? <BlockForm clubId={clubId} pitchId={pitchId} onDone={() => setAdding(false)} /> : null}
      {list}
    </section>
  );
}
