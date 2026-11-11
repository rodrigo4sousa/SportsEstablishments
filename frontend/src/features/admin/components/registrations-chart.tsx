"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { Bar, BarChart, CartesianGrid, LabelList, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { ChartDataTable } from "./chart-data-table";
import { LoadingRegion } from "@/components/loading-region";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import { useFormats } from "@/i18n/formats";
import { fetchRegistrations, registrationsQueryKey, type PeriodQuery } from "../api";
import { axisLabel, periodLabel, useAdminFailureMessage } from "../period-labels";
import type { RegistrationsGranularity } from "../types";

const GRANULARITIES = [
  { value: "week", label: "week" },
  { value: "month", label: "month" },
] as const satisfies ReadonlyArray<{ value: RegistrationsGranularity; label: string }>;

/** Few bars: each one says its number, so the series can be read without the colours (UX-027). A zero is left unsaid. */
const MAX_LABELLED_PERIODS = 12;

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
  const t = useTranslations("admin.registrations");
  const chart = useTranslations("admin.chart");
  const common = useTranslations("common");
  const formats = useFormats();
  const failureMessage = useAdminFailureMessage();
  const weekRange = (from: string, to: string) => chart("weekRange", { from, to });
  const valueLabel = (value: unknown) => (Number(value) > 0 ? formats.count(Number(value)) : "");
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
    axis: axisLabel(formats, unit, bucket),
    label: periodLabel(formats, weekRange, unit, bucket),
    owners: bucket.owners,
    players: bucket.players,
  }));

  const showValues = points.length <= MAX_LABELLED_PERIODS;

  return (
    <div className="flex flex-col gap-4">
      <div role="group" aria-label={t("groupBy")} className="flex w-fit rounded-md border">
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
            {chart(option.label)}
          </button>
        ))}
      </div>

      {registrations.isError ? (
        <div role="alert" className="flex flex-col items-start gap-3">
          <p>{failureMessage(registrations.error)}</p>
          <Button type="button" variant="outline" onClick={() => void registrations.refetch()}>
            {common("retry")}
          </Button>
        </div>
      ) : registrations.isPending ? (
        <LoadingRegion label={t("loading")}>
          <Skeleton className="h-72 w-full" />
        </LoadingRegion>
      ) : (
        <>
          <p role="status" className="text-sm text-muted-foreground">
            {t("summary", { owners: data!.owners, players: data!.players, from: formats.dateKey(data!.from), to: formats.dateKey(data!.to) })}
          </p>
          {data!.owners + data!.players === 0 ? (
            <p className="rounded-md border border-dashed px-4 py-3 text-sm text-muted-foreground">{t("empty")}</p>
          ) : null}
          <div role="img" aria-label={t("image")} className="h-72 w-full">
            <ResponsiveContainer width="100%" height="100%" initialDimension={{ width: 640, height: 288 }}>
              <BarChart data={points} margin={{ top: 8, right: 8, bottom: 8, left: 0 }}>
                <CartesianGrid vertical={false} strokeDasharray="3 3" />
                <XAxis dataKey="axis" tickLine={false} interval="preserveStartEnd" minTickGap={16} fontSize={12} />
                <YAxis allowDecimals={false} width={40} fontSize={12} />
                <Tooltip
                  formatter={(value, name) => [formats.count(Number(value)), String(name)]}
                  labelFormatter={(_, payload) => payload?.[0]?.payload?.label ?? ""}
                />
                <Legend formatter={(value) => <span className="text-foreground">{value}</span>} />
                <Bar dataKey="owners" name={t("owners")} fill="var(--chart-1)" radius={[3, 3, 0, 0]} isAnimationActive={false}>
                  {showValues ? <LabelList dataKey="owners" position="top" fontSize={11} formatter={valueLabel} /> : null}
                </Bar>
                <Bar dataKey="players" name={t("players")} fill="var(--chart-2)" radius={[3, 3, 0, 0]} isAnimationActive={false}>
                  {showValues ? <LabelList dataKey="players" position="top" fontSize={11} formatter={valueLabel} /> : null}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
          <ChartDataTable
            caption={t("tableCaption")}
            firstColumn={chart("periodColumn")}
            series={[t("owners"), t("players")]}
            rows={points.map((point) => ({ key: point.key, label: point.label, values: [point.owners, point.players] }))}
          />
        </>
      )}
    </div>
  );
}
