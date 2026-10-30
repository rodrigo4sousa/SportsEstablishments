"use client";

import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { LoadingRegion } from "@/components/loading-region";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { adminStatsQueryKey, fetchAdminStats } from "../api";
import type { AdminStats } from "../types";

const number = new Intl.NumberFormat("pt-PT");

type StatsCardsProps = {
  /** The totals the server already read for the first load; the browser shows them at once and refreshes them later. */
  initialStats: AdminStats | null;
};

/**
 * US24: the platform's totals as cards. Deactivated accounts and the clubs of deactivated owners are never added to the
 * big number: they sit under it, apart.
 */
export function StatsCards({ initialStats }: StatsCardsProps) {
  const stats = useQuery({
    queryKey: adminStatsQueryKey,
    queryFn: fetchAdminStats,
    initialData: initialStats ?? undefined,
    staleTime: 30_000,
  });

  if (stats.isPending) {
    return (
      <LoadingRegion label="A carregar os totais…" className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {[0, 1, 2, 3, 4].map((key) => (
          <Skeleton key={key} className="h-24 w-full rounded-lg" />
        ))}
      </LoadingRegion>
    );
  }
  if (stats.isError) {
    return (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{errorMessage(stats.error instanceof ApiError ? stats.error.code : "unknown_error")}</p>
        <Button type="button" variant="outline" onClick={() => void stats.refetch()}>
          Tentar novamente
        </Button>
      </div>
    );
  }

  const { owners, clubs, activePitches, players, confirmedBookings } = stats.data;
  return (
    <ul aria-label="Totais da plataforma" className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <Card label="Donos" value={owners.active}>
        <Detail value={owners.deactivated} singular="dono desativado" plural="donos desativados" />
      </Card>
      <Card label="Clubes visíveis" value={clubs.visible}>
        <Detail value={clubs.ofDeactivatedOwners} singular="clube de dono desativado" plural="clubes de donos desativados" />
        <Detail value={clubs.withoutActivePitches} singular="clube sem campos ativos" plural="clubes sem campos ativos" />
      </Card>
      <Card label="Campos ativos" value={activePitches}>
        <p className="text-sm text-muted-foreground">Só de donos ativos.</p>
      </Card>
      <Card label="Jogadores" value={players.active}>
        <Detail value={players.deactivated} singular="jogador desativado" plural="jogadores desativados" />
      </Card>
      <Card label="Reservas confirmadas" value={confirmedBookings}>
        <p className="text-sm text-muted-foreground">Passadas e futuras.</p>
      </Card>
    </ul>
  );
}

function Card({ label, value, children }: { label: string; value: number; children?: ReactNode }) {
  return (
    <li className="flex flex-col gap-1 rounded-lg border p-4">
      <h3 className="text-sm font-medium text-muted-foreground">{label}</h3>
      <p className="text-3xl font-semibold tabular-nums">{number.format(value)}</p>
      {children}
    </li>
  );
}

function Detail({ value, singular, plural }: { value: number; singular: string; plural: string }) {
  return (
    <p className="text-sm text-muted-foreground">
      <span className="tabular-nums">{number.format(value)}</span> {value === 1 ? singular : plural}
    </p>
  );
}
