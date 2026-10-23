"use client";

import Link from "next/link";
import { useState } from "react";
import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
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
      <p role="status" className="text-muted-foreground">
        A carregar os teus clubes…
      </p>
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
      <div className="flex justify-end">
        <Button asChild>
          <Link href="/manage/clubs/new">Novo clube</Link>
        </Button>
      </div>

      <ul className="flex flex-col divide-y rounded-md border">
        {items.map((club) => (
          <li key={club.id}>
            <Link href={`/manage/clubs/${club.id}`} className="flex flex-col gap-1 px-4 py-3 hover:bg-accent">
              <span className="font-medium">{club.name}</span>
              <span className="text-sm text-muted-foreground">
                {club.locality} · {club.address}
              </span>
            </Link>
          </li>
        ))}
      </ul>

      {pageCount > 1 ? (
        <nav aria-label="Páginas" className="flex items-center justify-between gap-4">
          <Button type="button" variant="outline" disabled={page <= 1} onClick={() => setPage(page - 1)}>
            Anterior
          </Button>
          <p className="text-sm text-muted-foreground">
            Página {page} de {pageCount}
          </p>
          <Button type="button" variant="outline" disabled={page >= pageCount} onClick={() => setPage(page + 1)}>
            Seguinte
          </Button>
        </nav>
      ) : null}
    </section>
  );
}
