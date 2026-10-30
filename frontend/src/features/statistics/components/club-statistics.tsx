"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useEffect } from "react";
import { EmptyState } from "@/components/empty-state";
import { PeriodFilter } from "@/components/period-filter";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { ApiError } from "@/lib/api/client";
import { loginUrl } from "@/lib/auth/redirects";
import { formatDateKey } from "@/lib/datetime";
import { ClubNotFound } from "@/features/clubs/components/club-not-found";
import { sportLabel } from "@/features/pitches/sports";
import { clubStatisticsQueryKey, fetchClubStatistics, type StatisticsRange } from "../api";
import { pitchFromParams, rangeFromParams, statisticsHref } from "../statistics-url";
import { StatisticsSkeleton, StatisticsTable, defaultRange, statisticsFailureMessage } from "./owner-statistics";

/**
 * US14: one club over a period, per pitch and in total, with a filter for one pitch. The period and the pitch are in the URL
 * (`?from=&to=&pitch=`). The pitch choices come from the club's unfiltered numbers, so choosing one does not make the others
 * disappear from the list. A club that is missing or another owner's is the same «not found» page as everywhere in the owner area.
 */
export function ClubStatistics({ clubId }: { clubId: string }) {
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const range = rangeFromParams(params);
  const pitchId = pitchFromParams(params);
  const fallback = defaultRange();

  const all = useQuery({
    queryKey: clubStatisticsQueryKey(clubId, range, null),
    queryFn: () => fetchClubStatistics(clubId, range, null),
    placeholderData: keepPreviousData,
    retry: false,
  });
  const one = useQuery({
    queryKey: clubStatisticsQueryKey(clubId, range, pitchId),
    queryFn: () => fetchClubStatistics(clubId, range, pitchId),
    enabled: pitchId !== null,
    placeholderData: keepPreviousData,
    retry: false,
  });
  const shown = pitchId === null ? all : one;
  const data = shown.data;

  const sessionEnded = [all.error, one.error].some((error) => error instanceof ApiError && error.status === 401);
  useEffect(() => {
    if (sessionEnded) {
      router.replace(loginUrl(statisticsHref(pathname, range, pitchId)));
    }
  }, [sessionEnded, router, pathname, range, pitchId]);

  const go = (nextRange: StatisticsRange, nextPitch: string | null) => router.replace(statisticsHref(pathname, nextRange, nextPitch), { scroll: false });

  if (all.error instanceof ApiError && all.error.code === "club_not_found") {
    return <ClubNotFound />;
  }

  const unknownPitch = one.error instanceof ApiError && one.error.code === "pitch_not_found";
  const error = unknownPitch ? null : (shown.error ?? all.error);

  return (
    <div className="flex flex-col gap-6">
      <p>
        <Link href={`/manage/clubs/${clubId}`} className="text-sm underline-offset-4 hover:underline">
          ← Voltar ao clube
        </Link>
      </p>
      <div className="flex flex-wrap items-end gap-4">
        <PeriodFilter
          idPrefix="club-stats"
          from={range.from}
          to={range.to}
          defaultFrom={all.data?.from ?? fallback.from}
          defaultTo={all.data?.to ?? fallback.to}
          onChange={(next) => go(next, pitchId)}
        />
        <div className="flex flex-col gap-1">
          <Label htmlFor="club-stats-pitch">Campo</Label>
          <NativeSelect id="club-stats-pitch" value={pitchId ?? ""} onChange={(event) => go(range, event.target.value || null)}>
            <option value="">Todos os campos</option>
            {(all.data?.pitches ?? []).map((pitch) => (
              <option key={pitch.pitchId} value={pitch.pitchId}>
                {pitch.name}
              </option>
            ))}
          </NativeSelect>
        </div>
      </div>

      {unknownPitch ? (
        <div role="alert" className="flex flex-col items-start gap-3">
          <p>Este campo já não existe neste clube.</p>
          <Button type="button" variant="outline" onClick={() => go(range, null)}>
            Ver todos os campos
          </Button>
        </div>
      ) : error && !sessionEnded ? (
        <div role="alert" className="flex flex-col items-start gap-3">
          <p>{statisticsFailureMessage(error)}</p>
          <Button type="button" variant="outline" onClick={() => void shown.refetch()}>
            Tentar novamente
          </Button>
        </div>
      ) : shown.isPending || sessionEnded || !data ? (
        <StatisticsSkeleton />
      ) : (
        <>
          <p role="status" className="text-sm text-muted-foreground">
            {data.clubName}, de {formatDateKey(data.from)} a {formatDateKey(data.to)}.
          </p>
          {data.pitches.length === 0 ? (
            <EmptyState title="Este clube não tem campos com horas abertas nem reservas neste período." />
          ) : (
            <StatisticsTable
              caption="Ocupação e receita por campo"
              firstColumn="Campo"
              rows={data.pitches.map((pitch) => ({
                key: pitch.pitchId,
                head: (
                  <>
                    {pitch.name}
                    <span className="block text-xs font-normal text-muted-foreground">
                      {sportLabel(pitch.sport)}
                      {pitch.status === "inactive" ? " · inativo" : ""}
                    </span>
                  </>
                ),
                figures: pitch,
              }))}
              total={data.total}
            />
          )}
        </>
      )}
    </div>
  );
}
