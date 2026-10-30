"use client";

import Link from "next/link";
import { useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { LoadingRegion } from "@/components/loading-region";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { errorMessage } from "@/lib/api/error-messages";
import { ApiError } from "@/lib/api/client";
import { CLUBS_PAGE_SIZE, clubListQueryKey, fetchClubs } from "../api";
import { NoClubsYet } from "./no-clubs-yet";

/** US06: the owner's clubs, by name, one page at a time. With none, it invites the owner to create the first. */
export function ClubList() {
  const [page, setPage] = useState(1);
  const clubs = useQuery({
    queryKey: clubListQueryKey(page),
    queryFn: () => fetchClubs(page),
    placeholderData: keepPreviousData,
  });

  if (clubs.isPending) {
    return (
      <LoadingRegion label="A carregar os teus clubes…" className="flex flex-col divide-y rounded-md border">
        {[0, 1, 2].map((row) => (
          <div key={row} className="flex flex-col gap-3 px-4 py-3">
            <Skeleton className="h-5 w-48" />
            <Skeleton className="h-4 w-64 max-w-full" />
            <Skeleton className="h-11 w-full sm:h-9 sm:w-72" />
          </div>
        ))}
      </LoadingRegion>
    );
  }

  if (clubs.isError) {
    const code = clubs.error instanceof ApiError ? clubs.error.code : "unknown_error";
    return (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{errorMessage(code)}</p>
        <Button type="button" variant="outline" onClick={() => void clubs.refetch()}>
          Tentar novamente
        </Button>
      </div>
    );
  }

  const { items, totalCount } = clubs.data;
  if (totalCount === 0) {
    return <NoClubsYet />;
  }

  const pageCount = Math.max(1, Math.ceil(totalCount / CLUBS_PAGE_SIZE));
  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-wrap justify-end gap-3">
        <Button asChild variant="outline" size="cta">
          <Link href="/manage/statistics">Estatísticas</Link>
        </Button>
        <Button asChild size="cta">
          <Link href="/manage/clubs/new">Novo clube</Link>
        </Button>
      </div>

      <ul className="flex flex-col divide-y rounded-md border">
        {items.map((club) => (
          <li key={club.id} className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
            <div className="flex min-w-0 flex-col gap-1">
              <h2 className="font-medium">{club.name}</h2>
              <p className="text-sm text-muted-foreground">
                {club.locality} · {club.address}
              </p>
            </div>
            {/* The calendar is what an owner opens most, so it is one tap from here (UX-005). The names carry the club for screen readers and start with the visible word. */}
            <div className="flex flex-wrap gap-2">
              <Button asChild size="cta" className="flex-1 sm:flex-none">
                <Link href={`/manage/clubs/${club.id}/calendar`} aria-label={`Calendário de ${club.name}`}>
                  Calendário
                </Link>
              </Button>
              <Button asChild size="cta" variant="outline" className="flex-1 sm:flex-none">
                <Link href={`/manage/clubs/${club.id}/statistics`} aria-label={`Estatísticas de ${club.name}`}>
                  Estatísticas
                </Link>
              </Button>
              <Button asChild size="cta" variant="outline" className="flex-1 sm:flex-none">
                <Link href={`/manage/clubs/${club.id}`} aria-label={`Editar ${club.name}`}>
                  Editar
                </Link>
              </Button>
            </div>
          </li>
        ))}
      </ul>

      {pageCount > 1 ? (
        <nav aria-label="Páginas" className="flex items-center justify-between gap-4">
          <Button type="button" variant="outline" size="cta" disabled={page <= 1} onClick={() => setPage(page - 1)}>
            Anterior
          </Button>
          <p className="text-sm text-muted-foreground">
            Página {page} de {pageCount}
          </p>
          <Button type="button" variant="outline" size="cta" disabled={page >= pageCount} onClick={() => setPage(page + 1)}>
            Seguinte
          </Button>
        </nav>
      ) : null}
    </section>
  );
}
