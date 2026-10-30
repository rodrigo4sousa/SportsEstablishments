"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { LoadingRegion } from "@/components/loading-region";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import { formatDateKey } from "@/lib/datetime";
import { fetchRegistrations, registrationsQueryKey, type PeriodQuery } from "../api";
import { axisLabel, failureMessage, periodLabel } from "../period-labels";
import type { RegistrationsGranularity } from "../types";

const GRANULARITIES: { value: RegistrationsGranularity; label: string }[] = [
  { value: "week", label: "Semana" },
  { value: "month", label: "Mês" },
];

const number = new Intl.NumberFormat("pt-PT");

type RegistrationsChartProps = {
  /** The period shared with the bookings chart and the top of the clubs. */
  period: PeriodQuery;
  granularity: RegistrationsGranularity;
  onGranularity: (granularity: RegistrationsGranularity) => void;
};

/**
 * US28: new owners and new players per week (from Monday) or month, by the Lisbon date they registered, side by side. The period is
 * the one of the bookings chart (US25); the API widens it to whole periods and the summary says which. Every period is there, with 0
 * where nobody registered. The same numbers are in a table for screen readers.
 */
export function RegistrationsChart({ period, granularity, onGranularity }: RegistrationsChartProps) {
  const query = { ...period, granularity };
  const registrations = useQuery({
    queryKey: registrationsQueryKey(query),
    queryFn: () => fetchRegistrations(query),
    placeholderData: keepPreviousData,
  });
  const data = registrations.data;
  const unit = data?.granularity ?? granularity;
  const points = (data?.buckets ?? []).map((bucket) => ({
    key: bucket.start,
    axis: axisLabel(unit, bucket),
    label: periodLabel(unit, bucket),
    owners: bucket.owners,
    players: bucket.players,
  }));

  return (
    <div className="flex flex-col gap-4">
      <div role="group" aria-label="Agrupar registos por" className="flex w-fit rounded-md border">
        {GRANULARITIES.map((option) => (
          <button
            key={option.value}
            type="button"
            aria-pressed={granularity === option.value}
            onClick={() => onGranularity(option.value)}
            className={
              granularity === option.value
                ? "bg-primary px-3 py-1.5 text-sm text-primary-foreground first:rounded-l-md last:rounded-r-md"
                : "px-3 py-1.5 text-sm text-muted-foreground first:rounded-l-md last:rounded-r-md hover:text-foreground"
            }
          >
            {option.label}
          </button>
        ))}
      </div>

      {registrations.isError ? (
        <div role="alert" className="flex flex-col items-start gap-3">
          <p>{failureMessage(registrations.error)}</p>
          <Button type="button" variant="outline" onClick={() => void registrations.refetch()}>
            Tentar novamente
          </Button>
        </div>
      ) : registrations.isPending ? (
        <LoadingRegion label="A carregar os registos…">
          <Skeleton className="h-72 w-full" />
        </LoadingRegion>
      ) : (
        <>
          <p role="status" className="text-sm text-muted-foreground">
            {data!.owners === 1 ? "1 dono" : `${number.format(data!.owners)} donos`} e{" "}
            {data!.players === 1 ? "1 jogador" : `${number.format(data!.players)} jogadores`} registados entre {formatDateKey(data!.from)} e{" "}
            {formatDateKey(data!.to)}.
          </p>
          {data!.owners + data!.players === 0 ? (
            <p className="rounded-md border border-dashed px-4 py-3 text-sm text-muted-foreground">Ainda não houve registos neste intervalo.</p>
          ) : null}
          <div role="img" aria-label="Gráfico de barras com os donos e os jogadores registados em cada período" className="h-72 w-full">
            <ResponsiveContainer width="100%" height="100%" initialDimension={{ width: 640, height: 288 }}>
              <BarChart data={points} margin={{ top: 8, right: 8, bottom: 8, left: 0 }}>
                <CartesianGrid vertical={false} strokeDasharray="3 3" />
                <XAxis dataKey="axis" tickLine={false} interval="preserveStartEnd" minTickGap={16} fontSize={12} />
                <YAxis allowDecimals={false} width={40} fontSize={12} />
                <Tooltip
                  formatter={(value, name) => [number.format(Number(value)), String(name)]}
                  labelFormatter={(_, payload) => payload?.[0]?.payload?.label ?? ""}
                />
                <Legend />
                <Bar dataKey="owners" name="Donos" fill="var(--chart-1)" radius={[3, 3, 0, 0]} isAnimationActive={false} />
                <Bar dataKey="players" name="Jogadores" fill="var(--chart-2)" radius={[3, 3, 0, 0]} isAnimationActive={false} />
              </BarChart>
            </ResponsiveContainer>
          </div>
          <table className="sr-only">
            <caption>Registos em cada período</caption>
            <thead>
              <tr>
                <th scope="col">Período</th>
                <th scope="col">Donos</th>
                <th scope="col">Jogadores</th>
              </tr>
            </thead>
            <tbody>
              {points.map((point) => (
                <tr key={point.key}>
                  <th scope="row">{point.label}</th>
                  <td>{point.owners}</td>
                  <td>{point.players}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </div>
  );
}
