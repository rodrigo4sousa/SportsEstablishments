"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { LoadingRegion } from "@/components/loading-region";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import { formatDateKey } from "@/lib/datetime";
import { formatHours, formatOccupation } from "@/features/statistics/format";
import { fetchTopOccupation, topOccupationQueryKey, type PeriodQuery } from "../api";
import { failureMessage } from "../period-labels";

/**
 * US27: the 10 clubs with the highest occupation in the period, with the formula of the owner's statistics (US14): booked hours over
 * the bookable hours. A club with nothing open in the period is not in the list. Shows the club, its owner's name and its locality,
 * and nothing about players. The period is the one shared with the charts.
 */
export function TopOccupationTable({ period }: { period: PeriodQuery }) {
  const top = useQuery({
    queryKey: topOccupationQueryKey(period),
    queryFn: () => fetchTopOccupation(period),
    placeholderData: keepPreviousData,
  });

  if (top.isError) {
    return (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{failureMessage(top.error)}</p>
        <Button type="button" variant="outline" onClick={() => void top.refetch()}>
          Tentar novamente
        </Button>
      </div>
    );
  }
  if (top.isPending) {
    return (
      <LoadingRegion label="A carregar os clubes…" className="flex flex-col gap-2">
        {[0, 1, 2, 3, 4].map((key) => (
          <Skeleton key={key} className="h-8 w-full" />
        ))}
      </LoadingRegion>
    );
  }

  const data = top.data;
  if (data.clubs.length === 0) {
    return (
      <p role="status" className="rounded-md border border-dashed px-4 py-6 text-muted-foreground">
        Nenhum clube teve horas abertas entre {formatDateKey(data.from)} e {formatDateKey(data.to)}.
      </p>
    );
  }

  return (
    <div className="flex flex-col gap-3">
      <p role="status" className="text-sm text-muted-foreground">
        De {formatDateKey(data.from)} a {formatDateKey(data.to)}.
      </p>
      <div className="overflow-x-auto rounded-lg border">
        <table className="w-full text-sm">
          <caption className="sr-only">Clubes com maior ocupação</caption>
          <thead className="border-b bg-muted/50 text-left">
            <tr>
              <th scope="col" className="px-3 py-2 text-right">#</th>
              <th scope="col" className="px-3 py-2">Clube</th>
              <th scope="col" className="px-3 py-2">Dono</th>
              <th scope="col" className="px-3 py-2">Localidade</th>
              <th scope="col" className="px-3 py-2 text-right">Ocupação</th>
              <th scope="col" className="px-3 py-2 text-right">Horas reservadas</th>
              <th scope="col" className="px-3 py-2 text-right">Horas abertas</th>
            </tr>
          </thead>
          <tbody>
            {data.clubs.map((club, index) => (
              <tr key={club.clubId} className="border-b last:border-b-0">
                <td className="px-3 py-2 text-right tabular-nums text-muted-foreground">{index + 1}</td>
                <th scope="row" className="px-3 py-2 text-left font-medium">
                  <Link href={`/clubs/${club.slug}`} className="underline-offset-4 hover:underline">
                    {club.clubName}
                  </Link>
                </th>
                <td className="px-3 py-2">{club.ownerName}</td>
                <td className="px-3 py-2">{club.locality}</td>
                <td className="px-3 py-2 text-right tabular-nums">{formatOccupation(club.occupation)}</td>
                <td className="px-3 py-2 text-right tabular-nums">{formatHours(club.bookedHours)}</td>
                <td className="px-3 py-2 text-right tabular-nums">{formatHours(club.openHours)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
