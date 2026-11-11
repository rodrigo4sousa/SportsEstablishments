"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { useState } from "react";
import { useTranslations } from "next-intl";
import { LoadingRegion } from "@/components/loading-region";
import { ScrollRegion } from "@/components/scroll-region";
import { NativeSelect } from "@/components/ui/native-select";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/empty-state";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useFormats } from "@/i18n/formats";
import { useAdminFailureMessage } from "../period-labels";
import { adminOwnersQueryKey, fetchAdminOwners, type OwnersQuery } from "../api";
import type { OwnerSort } from "../types";

const COLUMNS = [
  { sort: "name", label: "name", numeric: false },
  { sort: "email", label: "email", numeric: false },
  { sort: "registeredAt", label: "registered", numeric: false },
  { sort: "status", label: "status", numeric: false },
  { sort: "clubs", label: "clubs", numeric: true },
  { sort: "pitches", label: "pitches", numeric: true },
  { sort: "bookings", label: "bookings", numeric: true },
  { sort: "players", label: "players", numeric: true },
] as const satisfies ReadonlyArray<{ sort: OwnerSort; label: string; numeric: boolean }>;

type OwnersTableProps = {
  query: OwnersQuery;
  onChange: (patch: Partial<OwnersQuery>) => void;
};

/**
 * US26: the owners and their numbers, 20 to a page. A header sorts by its column (a second click reverses it), the
 * search finds part of a name or an email, and both, with the page, are in the URL. Deactivated owners are marked.
 * The only people shown are owners: players appear here only as a count.
 */
export function OwnersTable({ query, onChange }: OwnersTableProps) {
  const t = useTranslations("admin.ownersTable");
  const common = useTranslations("common");
  const formats = useFormats();
  const failureMessage = useAdminFailureMessage();
  const owners = useQuery({
    queryKey: adminOwnersQueryKey(query),
    queryFn: () => fetchAdminOwners(query),
    placeholderData: keepPreviousData,
  });
  // The text being typed; it becomes the search (and the URL) when the form is sent.
  const [typed, setTyped] = useState(query.search);
  const [seenSearch, setSeenSearch] = useState(query.search);
  if (seenSearch !== query.search) {
    // The URL changed under us (back button, a link): the field follows it.
    setSeenSearch(query.search);
    setTyped(query.search);
  }

  const sortBy = (sort: OwnerSort) => {
    onChange({ sort, direction: query.sort === sort && query.direction === "asc" ? "desc" : "asc", page: 1 });
  };

  const data = owners.data;
  const pages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  return (
    <div className="flex flex-col gap-4">
      <form
        role="search"
        className="flex flex-wrap items-end gap-3"
        onSubmit={(event) => {
          event.preventDefault();
          onChange({ search: typed.trim(), page: 1 });
        }}
      >
        <div className="flex flex-col gap-1">
          <Label htmlFor="owners-search">{t("search")}</Label>
          <Input
            id="owners-search"
            type="search"
            maxLength={100}
            placeholder={t("searchPlaceholder")}
            value={typed}
            onChange={(event) => setTyped(event.target.value)}
            className="w-72"
          />
        </div>
        <Button type="submit">{t("searchButton")}</Button>
        {query.search ? (
          <Button
            type="button"
            variant="outline"
            onClick={() => {
              setTyped("");
              onChange({ search: "", page: 1 });
            }}
          >
            {t("clear")}
          </Button>
        ) : null}
      </form>

      {owners.isError ? (
        <div role="alert" className="flex flex-col items-start gap-3">
          <p>{failureMessage(owners.error)}</p>
          <Button type="button" variant="outline" onClick={() => void owners.refetch()}>
            {common("retry")}
          </Button>
        </div>
      ) : owners.isPending ? (
        <LoadingRegion label={t("loading")} className="flex flex-col gap-2">
          {[0, 1, 2, 3, 4].map((key) => (
            <Skeleton key={key} className="h-8 w-full" />
          ))}
        </LoadingRegion>
      ) : data!.totalCount === 0 ? (
        <EmptyState title={query.search ? t("noMatch") : t("none")} />
      ) : data!.items.length === 0 ? (
        <div role="status" className="flex flex-col items-start gap-3">
          <p className="text-muted-foreground">{t("emptyPage")}</p>
          <Button type="button" variant="outline" onClick={() => onChange({ page: 1 })}>
            {t("firstPage")}
          </Button>
        </div>
      ) : (
        <>
          <p role="status" className="text-sm text-muted-foreground">
            {t("count", { count: data!.totalCount })}
          </p>
          {/* A phone has no column headers to press, so the order is chosen here (UX-029). */}
          <div className="flex flex-wrap items-end gap-3 sm:hidden">
            <div className="flex flex-col gap-1">
              <Label htmlFor="owners-sort">{t("sortBy")}</Label>
              <NativeSelect id="owners-sort" value={query.sort} onChange={(event) => onChange({ sort: event.target.value as OwnerSort, direction: "asc", page: 1 })}>
                {COLUMNS.map((column) => (
                  <option key={column.sort} value={column.sort}>
                    {t(column.label)}
                  </option>
                ))}
              </NativeSelect>
            </div>
            <Button type="button" variant="outline" size="cta" onClick={() => onChange({ direction: query.direction === "asc" ? "desc" : "asc", page: 1 })}>
              {query.direction === "asc" ? t("ascending") : t("descending")}
            </Button>
          </div>
          <ScrollRegion label={t("region")}>
            <table role="table" className="w-full text-sm max-sm:block">
              <caption className="sr-only">{t("region")}</caption>
              <thead role="rowgroup" className="border-b bg-muted/50 text-left max-sm:hidden">
                <tr role="row">
                  {COLUMNS.map((column) => (
                    <th
                      key={column.sort}
                      role="columnheader"
                      scope="col"
                      aria-sort={query.sort === column.sort ? (query.direction === "asc" ? "ascending" : "descending") : "none"}
                      className={column.numeric ? "px-3 py-2 text-right" : "px-3 py-2"}
                    >
                      <button type="button" onClick={() => sortBy(column.sort)} className="-my-2 py-2 font-medium hover:underline">
                        {t(column.label)}
                        <span aria-hidden="true">{query.sort === column.sort ? (query.direction === "asc" ? " ↑" : " ↓") : ""}</span>
                      </button>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody role="rowgroup" className="max-sm:block max-sm:p-3 max-sm:pb-0">
                {data!.items.map((owner) => (
                  <tr key={owner.id} role="row" className="border-b last:border-b-0 max-sm:mb-3 max-sm:block max-sm:rounded-md max-sm:border max-sm:p-3 max-sm:last:border-b">
                    <th role="rowheader" scope="row" className="px-3 py-2 text-left font-medium max-sm:block max-sm:px-0 max-sm:pt-0 max-sm:text-base">
                      <Link href={`/admin/owners/${owner.id}`} className="underline underline-offset-4 decoration-muted-foreground hover:decoration-foreground">
                        {owner.name}
                      </Link>
                    </th>
                    <td role="cell" data-label={t("email")} className="px-3 py-2 max-sm:flex max-sm:justify-between max-sm:gap-3 max-sm:px-0 max-sm:py-0.5 max-sm:text-left max-sm:before:text-muted-foreground max-sm:before:content-[attr(data-label)]">{owner.email}</td>
                    <td role="cell" data-label={t("registered")} className="px-3 py-2 whitespace-nowrap max-sm:flex max-sm:justify-between max-sm:gap-3 max-sm:px-0 max-sm:py-0.5 max-sm:text-left max-sm:before:text-muted-foreground max-sm:before:content-[attr(data-label)]">{formats.date(owner.registeredAt)}</td>
                    <td role="cell" data-label={t("status")} className="px-3 py-2 max-sm:flex max-sm:justify-between max-sm:gap-3 max-sm:px-0 max-sm:py-0.5 max-sm:text-left max-sm:before:text-muted-foreground max-sm:before:content-[attr(data-label)]">
                      {owner.deactivated ? (
                        <span className="rounded-full border border-destructive/40 bg-destructive/10 px-2 py-0.5 text-xs text-destructive">{t("deactivated")}</span>
                      ) : (
                        <span className="text-muted-foreground">{t("active")}</span>
                      )}
                    </td>
                    <td role="cell" data-label={t("clubs")} className="px-3 py-2 text-right tabular-nums max-sm:flex max-sm:justify-between max-sm:gap-3 max-sm:px-0 max-sm:py-0.5 max-sm:text-left max-sm:before:text-muted-foreground max-sm:before:content-[attr(data-label)]">{formats.count(owner.clubs)}</td>
                    <td role="cell" data-label={t("pitches")} className="px-3 py-2 text-right tabular-nums max-sm:flex max-sm:justify-between max-sm:gap-3 max-sm:px-0 max-sm:py-0.5 max-sm:text-left max-sm:before:text-muted-foreground max-sm:before:content-[attr(data-label)]">{formats.count(owner.pitches)}</td>
                    <td role="cell" data-label={t("bookings")} className="px-3 py-2 text-right tabular-nums max-sm:flex max-sm:justify-between max-sm:gap-3 max-sm:px-0 max-sm:py-0.5 max-sm:text-left max-sm:before:text-muted-foreground max-sm:before:content-[attr(data-label)]">{formats.count(owner.bookings)}</td>
                    <td role="cell" data-label={t("players")} className="px-3 py-2 text-right tabular-nums max-sm:flex max-sm:justify-between max-sm:gap-3 max-sm:px-0 max-sm:py-0.5 max-sm:text-left max-sm:before:text-muted-foreground max-sm:before:content-[attr(data-label)]">{formats.count(owner.players)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </ScrollRegion>
          {pages > 1 ? (
            <nav aria-label={t("pages")} className="flex items-center justify-between gap-3">
              <Button type="button" variant="outline" disabled={data!.page <= 1} onClick={() => onChange({ page: data!.page - 1 })}>
                {common("previous")}
              </Button>
              <p className="text-sm text-muted-foreground">{common("pageOf", { page: data!.page, pages })}</p>
              <Button type="button" variant="outline" disabled={data!.page >= pages} onClick={() => onChange({ page: data!.page + 1 })}>
                {common("next")}
              </Button>
            </nav>
          ) : null}
        </>
      )}
    </div>
  );
}
