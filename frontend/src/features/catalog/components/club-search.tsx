"use client";

import { keepPreviousData, useQuery, type UseQueryResult } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { SPORTS, sportLabel } from "@/features/pitches/sports";
import { DATE_OUT_OF_WINDOW, previewDateRange } from "@/features/pitches/schedule";
import { ApiError } from "@/lib/api/client";
import { formatDateKeyLong, isDateKey } from "@/lib/datetime";
import { fetchLocalities, localitiesQueryKey, searchClubs, searchQueryKey } from "../api";
import { publicErrorMessage } from "../messages";
import { clubHref, filtersFromParams, searchHref } from "../search-filters";
import type { ClubSearchItem, ClubSearchPage, SearchFilters } from "../types";

const ALL_SPORTS = "Todos os desportos";
const ALL_LOCALITIES = "Todas as localidades";

/**
 * US16: search the clubs that can take bookings by sport, locality and date (today by default, up to 30 days ahead).
 * Open to everyone: no session is needed. The search lives in the URL (`?sport=&locality=&date=&page=`), so it can be
 * shared and the back button works; the results come from the API, 20 per page, with a free-slot indicator for the date.
 */
export function ClubSearch() {
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
      <form noValidate aria-label="Pesquisar clubes" className="grid gap-4 sm:grid-cols-3" onSubmit={(event) => event.preventDefault()}>
        <div className="flex flex-col gap-2">
          <Label htmlFor="search-sport">Desporto</Label>
          <NativeSelect id="search-sport" value={filters.sport} onChange={(event) => change({ sport: event.target.value })}>
            <option value="">{ALL_SPORTS}</option>
            {SPORTS.map((sport) => (
              <option key={sport.code} value={sport.code}>
                {sport.label}
              </option>
            ))}
          </NativeSelect>
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="search-locality">Localidade</Label>
          <NativeSelect id="search-locality" value={filters.locality} onChange={(event) => change({ locality: event.target.value })}>
            <option value="">{ALL_LOCALITIES}</option>
            {options.map((locality) => (
              <option key={locality} value={locality}>
                {locality}
              </option>
            ))}
          </NativeSelect>
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="search-date">Data</Label>
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
          {shownDate === "" ? "Escolhe uma data." : DATE_OUT_OF_WINDOW}
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
  if (query.isPending) {
    return (
      <p role="status" className="text-muted-foreground">
        A procurar clubes…
      </p>
    );
  }

  if (query.isError) {
    const code = query.error instanceof ApiError ? query.error.code : "unknown_error";
    return (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{publicErrorMessage(code)}</p>
        <Button type="button" variant="outline" onClick={onRetry}>
          Tentar novamente
        </Button>
      </div>
    );
  }

  const { items, page, pageSize, totalCount } = query.data;
  if (totalCount === 0) {
    return (
      <p role="status" className="rounded-md border border-dashed px-4 py-6 text-muted-foreground">
        Não encontrámos clubes com estes filtros. Experimenta outra localidade, outro desporto ou outra data.
      </p>
    );
  }

  const pages = Math.max(1, Math.ceil(totalCount / pageSize));
  if (items.length === 0) {
    return (
      <div role="status" className="flex flex-col items-start gap-3">
        <p className="text-muted-foreground">Esta página não tem resultados.</p>
        <Button type="button" variant="outline" onClick={() => onPage(1)}>
          Voltar à primeira página
        </Button>
      </div>
    );
  }

  return (
    <section aria-label="Resultados" className="flex flex-col gap-4">
      <p role="status" className="text-sm text-muted-foreground">
        {totalCount === 1 ? "1 clube" : `${totalCount} clubes`} · {formatDateKeyLong(date)}
      </p>
      <ul aria-label="Clubes" className="flex flex-col gap-3">
        {items.map((club) => (
          <ClubResult key={club.slug} club={club} filters={filters} />
        ))}
      </ul>
      {pages > 1 ? (
        <nav aria-label="Páginas" className="flex items-center justify-between gap-3">
          <Button type="button" variant="outline" disabled={page <= 1} onClick={() => onPage(page - 1)}>
            Anterior
          </Button>
          <p className="text-sm text-muted-foreground">
            Página {page} de {pages}
          </p>
          <Button type="button" variant="outline" disabled={page >= pages} onClick={() => onPage(page + 1)}>
            Seguinte
          </Button>
        </nav>
      ) : null}
    </section>
  );
}

function ClubResult({ club, filters }: { club: ClubSearchItem; filters: SearchFilters }) {
  return (
    <li className="flex flex-col gap-2 rounded-lg border p-4 sm:flex-row sm:items-center sm:justify-between">
      <div className="flex flex-col gap-1">
        <h3 className="font-medium">
          <Link href={clubHref(club.slug, filters)} className="underline-offset-4 hover:underline">
            {club.name}
          </Link>
        </h3>
        <p className="text-sm text-muted-foreground">
          {club.locality} · {club.sports.map(sportLabel).join(", ")}
        </p>
      </div>
      {club.hasFreeSlots ? (
        <p className="text-sm font-medium text-emerald-700 dark:text-emerald-400">Há horários livres</p>
      ) : (
        <p className="text-sm text-muted-foreground">Sem horários livres</p>
      )}
    </li>
  );
}
