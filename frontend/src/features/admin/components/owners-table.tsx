"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { formatLisbonDate } from "@/lib/datetime";
import { adminOwnersQueryKey, fetchAdminOwners, type OwnersQuery } from "../api";
import type { OwnerSort } from "../types";

const number = new Intl.NumberFormat("pt-PT");

const COLUMNS: { sort: OwnerSort; label: string; numeric: boolean }[] = [
  { sort: "name", label: "Nome", numeric: false },
  { sort: "email", label: "Email", numeric: false },
  { sort: "registeredAt", label: "Registo", numeric: false },
  { sort: "status", label: "Estado", numeric: false },
  { sort: "clubs", label: "Clubes", numeric: true },
  { sort: "pitches", label: "Campos", numeric: true },
  { sort: "bookings", label: "Reservas", numeric: true },
  { sort: "players", label: "Jogadores", numeric: true },
];

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
          <Label htmlFor="owners-search">Pesquisar dono</Label>
          <Input
            id="owners-search"
            type="search"
            maxLength={100}
            placeholder="Nome ou email"
            value={typed}
            onChange={(event) => setTyped(event.target.value)}
            className="w-72"
          />
        </div>
        <Button type="submit">Pesquisar</Button>
        {query.search ? (
          <Button
            type="button"
            variant="outline"
            onClick={() => {
              setTyped("");
              onChange({ search: "", page: 1 });
            }}
          >
            Limpar
          </Button>
        ) : null}
      </form>

      {owners.isError ? (
        <div role="alert" className="flex flex-col items-start gap-3">
          <p>{owners.error instanceof ApiError ? (Object.values(owners.error.fieldErrors).flat()[0] ?? errorMessage(owners.error.code)) : errorMessage("unknown_error")}</p>
          <Button type="button" variant="outline" onClick={() => void owners.refetch()}>
            Tentar novamente
          </Button>
        </div>
      ) : owners.isPending ? (
        <p role="status" className="text-muted-foreground">
          A carregar os donos…
        </p>
      ) : data!.totalCount === 0 ? (
        <p role="status" className="rounded-md border border-dashed px-4 py-6 text-muted-foreground">
          {query.search ? "Nenhum dono corresponde à pesquisa." : "Ainda não há donos registados."}
        </p>
      ) : data!.items.length === 0 ? (
        <div role="status" className="flex flex-col items-start gap-3">
          <p className="text-muted-foreground">Esta página não tem donos.</p>
          <Button type="button" variant="outline" onClick={() => onChange({ page: 1 })}>
            Voltar à primeira página
          </Button>
        </div>
      ) : (
        <>
          <p role="status" className="text-sm text-muted-foreground">
            {data!.totalCount === 1 ? "1 dono" : `${number.format(data!.totalCount)} donos`}
          </p>
          <div className="overflow-x-auto rounded-lg border">
            <table className="w-full text-sm">
              <caption className="sr-only">Donos e os seus números</caption>
              <thead className="border-b bg-muted/50 text-left">
                <tr>
                  {COLUMNS.map((column) => (
                    <th
                      key={column.sort}
                      scope="col"
                      aria-sort={query.sort === column.sort ? (query.direction === "asc" ? "ascending" : "descending") : "none"}
                      className={column.numeric ? "px-3 py-2 text-right" : "px-3 py-2"}
                    >
                      <button type="button" onClick={() => sortBy(column.sort)} className="font-medium hover:underline">
                        {column.label}
                        <span aria-hidden="true">{query.sort === column.sort ? (query.direction === "asc" ? " ↑" : " ↓") : ""}</span>
                      </button>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {data!.items.map((owner) => (
                  <tr key={owner.id} className="border-b last:border-b-0">
                    <th scope="row" className="px-3 py-2 text-left font-medium">
                      {owner.name}
                    </th>
                    <td className="px-3 py-2">{owner.email}</td>
                    <td className="px-3 py-2 whitespace-nowrap">{formatLisbonDate(owner.registeredAt)}</td>
                    <td className="px-3 py-2">
                      {owner.deactivated ? (
                        <span className="rounded-full border border-destructive/40 bg-destructive/10 px-2 py-0.5 text-xs text-destructive">Desativado</span>
                      ) : (
                        <span className="text-muted-foreground">Ativo</span>
                      )}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums">{number.format(owner.clubs)}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{number.format(owner.pitches)}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{number.format(owner.bookings)}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{number.format(owner.players)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {pages > 1 ? (
            <nav aria-label="Páginas de donos" className="flex items-center justify-between gap-3">
              <Button type="button" variant="outline" disabled={data!.page <= 1} onClick={() => onChange({ page: data!.page - 1 })}>
                Anterior
              </Button>
              <p className="text-sm text-muted-foreground">
                Página {data!.page} de {pages}
              </p>
              <Button type="button" variant="outline" disabled={data!.page >= pages} onClick={() => onChange({ page: data!.page + 1 })}>
                Seguinte
              </Button>
            </nav>
          ) : null}
        </>
      )}
    </div>
  );
}
