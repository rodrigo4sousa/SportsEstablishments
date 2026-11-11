"use client";

import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { useTranslations } from "next-intl";
import { LoadingRegion } from "@/components/loading-region";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api/client";
import { UNKNOWN_ERROR, useApiErrors } from "@/lib/api/error-messages";
import { useFormats } from "@/i18n/formats";
import { adminStatsQueryKey, fetchAdminStats } from "../api";
import type { AdminStats } from "../types";

type StatsCardsProps = {
  /** The totals the server already read for the first load; the browser shows them at once and refreshes them later. */
  initialStats: AdminStats | null;
};

/**
 * US24: the platform's totals as cards. Deactivated accounts and the clubs of deactivated owners are never added to the
 * big number: they sit under it, apart.
 */
export function StatsCards({ initialStats }: StatsCardsProps) {
  const t = useTranslations("admin");
  const common = useTranslations("common");
  const apiErrors = useApiErrors();
  const stats = useQuery({
    queryKey: adminStatsQueryKey,
    queryFn: fetchAdminStats,
    initialData: initialStats ?? undefined,
    staleTime: 30_000,
  });

  if (stats.isPending) {
    return (
      <LoadingRegion label={t("loadingTotals")} className="grid grid-cols-2 gap-3 sm:gap-4 lg:grid-cols-3">
        {[0, 1, 2, 3, 4].map((key) => (
          <Skeleton key={key} className="h-24 w-full rounded-lg" />
        ))}
      </LoadingRegion>
    );
  }
  if (stats.isError) {
    return (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{apiErrors.message(stats.error instanceof ApiError ? stats.error.code : UNKNOWN_ERROR)}</p>
        <Button type="button" variant="outline" onClick={() => void stats.refetch()}>
          {common("retry")}
        </Button>
      </div>
    );
  }

  const { owners, clubs, activePitches, players, confirmedBookings } = stats.data;
  return (
    <ul aria-label={t("totalsList")} className="grid grid-cols-2 gap-3 sm:gap-4 lg:grid-cols-3">
      <Card label={t("owners")} value={owners.active}>
        <Detail message="deactivatedOwners" value={owners.deactivated} />
      </Card>
      <Card label={t("visibleClubs")} value={clubs.visible}>
        <Detail message="clubsOfDeactivated" value={clubs.ofDeactivatedOwners} />
        <Detail message="clubsWithoutPitches" value={clubs.withoutActivePitches} />
      </Card>
      <Card label={t("activePitches")} value={activePitches}>
        <p className="text-sm text-muted-foreground">{t("onlyActiveOwners")}</p>
      </Card>
      <Card label={t("players")} value={players.active}>
        <Detail message="deactivatedPlayers" value={players.deactivated} />
      </Card>
      <Card label={t("confirmedBookings")} value={confirmedBookings}>
        <p className="text-sm text-muted-foreground">{t("pastAndFuture")}</p>
      </Card>
    </ul>
  );
}

function Card({ label, value, children }: { label: string; value: number; children?: ReactNode }) {
  const formats = useFormats();
  return (
    <li className="flex flex-col gap-1 rounded-lg border p-4">
      <h3 className="text-sm font-medium text-muted-foreground">{label}</h3>
      <p className="text-3xl font-semibold tabular-nums">{formats.count(value)}</p>
      {children}
    </li>
  );
}

type DetailMessage = "deactivatedOwners" | "clubsOfDeactivated" | "clubsWithoutPitches" | "deactivatedPlayers";

function Detail({ message, value }: { message: DetailMessage; value: number }) {
  const t = useTranslations("admin");
  return (
    <p className="text-sm text-muted-foreground">
      {t.rich(message, { count: value, num: (chunks) => <span className="tabular-nums">{chunks}</span> })}
    </p>
  );
}
