"use client";

import { keepPreviousData, useQuery, type UseQueryResult } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { LoadingRegion } from "@/components/loading-region";
import { Skeleton } from "@/components/ui/skeleton";
import { EmptyState } from "@/components/empty-state";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { SPORTS, useSportLabel } from "@/features/pitches/sports";
import { BOOKING_DAYS_AHEAD, previewDateRange } from "@/features/pitches/schedule";
import { useFormats } from "@/i18n/formats";
import { ApiError } from "@/lib/api/client";
import { UNKNOWN_ERROR } from "@/lib/api/error-messages";
import { isDateKey } from "@/lib/datetime";
import { fetchLocalities, localitiesQueryKey, searchClubs, searchQueryKey } from "../api";
import { usePublicErrorMessage } from "../messages";
import { clubHref, filtersFromParams, searchHref } from "../search-filters";
import type { ClubSearchItem, ClubSearchPage, SearchFilters } from "../types";

/**
 * US16: search the clubs that can take bookings by sport, locality and date (today by default, up to 30 days ahead).
 * Open to everyone: no session is needed. The search lives in the URL (`?sport=&locality=&date=&page=`), so it can be
 * shared and the back button works; the results come from the API, 20 per page, with a free-slot indicator for the date.
 */
export function ClubSearch() {
  const t = useTranslations("catalog.search");
  const dates = useTranslations("dates");
  const sportLabel = useSportLabel();
  const router = useRouter();
  const pathname = usePathname();
  const params = useSearchParams();
  const [range] = useState(() => previewDateRange());
  const filters = filtersFromParams(params);

  const shownDate = filters.date || range.min;
  const dateInWindow = isDateKey(shownDate) && shownDate >= range.min && shownDate <= range.max;

  const localities = useQuery({ queryKey: localitiesQueryKey, queryFn: fetchLocalities, staleTime: 5 * 60_000 });
  const results = useQuery({
    queryKey: searchQueryKey(filters),
    queryFn: () => searchClubs(filters),
    enabled: dateInWindow,
    placeholderData: keepPreviousData,
  });

  // Changing a filter goes back to the first page.
  const change = (patch: Partial<SearchFilters>) => router.replace(searchHref(pathname, { ...filters, page: 1, ...patch }), { scroll: false });
  const goToPage = (page: number) => router.replace(searchHref(pathname, { ...filters, page }), { scroll: false });

  // A locality from the link that the list does not have (the club may be gone) still has to show as chosen.
  const localityOptions = localities.data ?? [];
  const options = filters.locality && !localityOptions.includes(filters.locality) ? [filters.locality, ...localityOptions] : localityOptions;

  return (
    <div className="flex flex-col gap-6">
      <form noValidate aria-label={t("form")} className="grid gap-4 sm:grid-cols-3" onSubmit={(event) => event.preventDefault()}>
        <div className="flex flex-col gap-2">
          <Label htmlFor="search-sport">{t("sport")}</Label>
          <NativeSelect id="search-sport" value={filters.sport} onChange={(event) => change({ sport: event.target.value })}>
            <option value="">{t("allSports")}</option>
            {SPORTS.map((sport) => (
              <option key={sport.code} value={sport.code}>
                {sportLabel(sport.code)}
              </option>
            ))}
          </NativeSelect>
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="search-locality">{t("locality")}</Label>
          <NativeSelect id="search-locality" value={filters.locality} onChange={(event) => change({ locality: event.target.value })}>
            <option value="">{t("allLocalities")}</option>
            {options.map((locality) => (
              <option key={locality} value={locality}>
                {locality}
              </option>
            ))}
          </NativeSelect>
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="search-date">{t("date")}</Label>
          <Input
            id="search-date"
            type="date"
            min={range.min}
            max={range.max}
            value={shownDate}
            aria-invalid={dateInWindow ? undefined : true}
            aria-describedby={dateInWindow ? undefined : "search-date-error"}
            onChange={(event) => change({ date: event.target.value })}
          />
        </div>
      </form>

      {dateInWindow ? (
        <Results
          query={results}
          filters={filters}
          date={shownDate}
          onPage={goToPage}
          onRetry={() => void results.refetch()}
        />
      ) : (
        <p id="search-date-error" role="alert" className="text-sm text-destructive">
          {shownDate === "" ? dates("chooseDate") : dates("outOfWindow", { days: BOOKING_DAYS_AHEAD })}
        </p>
      )}
    </div>
  );
}

type ResultsProps = {
  query: UseQueryResult<ClubSearchPage>;
  filters: SearchFilters;
  date: string;
  onPage: (page: number) => void;
  onRetry: () => void;
};

function Results({ query, filters, date, onPage, onRetry }: ResultsProps) {
  const t = useTranslations("catalog.search");
  const common = useTranslations("common");
  const publicErrorMessage = usePublicErrorMessage();
  const formats = useFormats();
  if (query.isPending) {
    return (
      <LoadingRegion label={t("searching")} className="flex flex-col gap-3">
        {[0, 1, 2].map((key) => (
          <Skeleton key={key} className="h-20 w-full rounded-lg" />
        ))}
      </LoadingRegion>
    );
  }

  if (query.isError) {
    const code = query.error instanceof ApiError ? query.error.code : UNKNOWN_ERROR;
    return (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{publicErrorMessage(code)}</p>
        <Button type="button" size="cta" variant="outline" onClick={onRetry}>
          {common("retry")}
        </Button>
      </div>
    );
  }

  const { items, page, pageSize, totalCount } = query.data;
  if (totalCount === 0) {
    return (
      <EmptyState title={t("emptyTitle")} description={t("emptyDescription")} />
    );
  }

  const pages = Math.max(1, Math.ceil(totalCount / pageSize));
  if (items.length === 0) {
    return (
      <div role="status" className="flex flex-col items-start gap-3">
        <p className="text-muted-foreground">{t("emptyPage")}</p>
        <Button type="button" size="cta" variant="outline" onClick={() => onPage(1)}>
          {t("firstPage")}
        </Button>
      </div>
    );
  }

  return (
    <section aria-label={t("results")} className="flex flex-col gap-4">
      {/* UX-017: the club names below are h3, so the page needs an h2 between them and its h1. */}
      <h2 className="sr-only">{t("found")}</h2>
      <p role="status" className="text-sm text-muted-foreground">
        {t("count", { count: totalCount, date: formats.dateKeyLong(date) })}
      </p>
      <ul aria-label={t("clubs")} className="flex flex-col gap-3">
        {items.map((club) => (
          <ClubResult key={club.slug} club={club} filters={filters} />
        ))}
      </ul>
      {pages > 1 ? (
        <nav aria-label={common("pages")} className="flex items-center justify-between gap-3">
          <Button type="button" size="cta" variant="outline" disabled={page <= 1} onClick={() => onPage(page - 1)}>
            {common("previous")}
          </Button>
          <p className="text-sm text-muted-foreground">{common("pageOf", { page, pages })}</p>
          <Button type="button" size="cta" variant="outline" disabled={page >= pages} onClick={() => onPage(page + 1)}>
            {common("next")}
          </Button>
        </nav>
      ) : null}
    </section>
  );
}

/**
 * UX-009: the whole card opens the club, not just its name. The link is the name (so a screen reader reads one clear link per
 * club) and its ::after covers the card; the focus ring is drawn around the card, not around the words.
 */
function ClubResult({ club, filters }: { club: ClubSearchItem; filters: SearchFilters }) {
  const t = useTranslations("catalog.search");
  const sportLabel = useSportLabel();
  return (
    <li className="relative flex flex-col gap-2 rounded-lg border p-4 transition-colors hover:bg-accent sm:flex-row sm:items-center sm:justify-between">
      <div className="flex flex-col gap-1">
        <h3 className="font-medium">
          <Link
            href={clubHref(club.slug, filters)}
            className="underline-offset-4 after:absolute after:inset-0 after:rounded-lg hover:underline focus-visible:outline-transparent focus-visible:after:outline-2 focus-visible:after:outline-solid focus-visible:after:outline-offset-2 focus-visible:after:outline-ring"
          >
            {club.name}
          </Link>
        </h3>
        <p className="text-sm text-muted-foreground">
          {club.locality} · {club.sports.map(sportLabel).join(", ")}
        </p>
      </div>
      {club.hasFreeSlots ? (
        <p className="text-sm font-medium text-emerald-700 dark:text-emerald-400">{t("hasFreeSlots")}</p>
      ) : (
        <p className="text-sm text-muted-foreground">{t("noFreeSlots")}</p>
      )}
    </li>
  );
}
