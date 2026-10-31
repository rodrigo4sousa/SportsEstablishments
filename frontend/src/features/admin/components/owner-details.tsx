"use client";

import { useQuery } from "@tanstack/react-query";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { EmptyState } from "@/components/empty-state";
import { LoadingRegion } from "@/components/loading-region";
import { ResultNotice } from "@/components/result-notice";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { loginUrl } from "@/lib/auth/redirects";
import { useDocumentTitle } from "@/lib/document-title";
import { formatLisbonDate, formatLisbonDateTime } from "@/lib/datetime";
import { useRestoreFocus } from "@/lib/focus";
import { adminOwnerHistoryQueryKey, adminOwnerQueryKey, fetchAdminOwner, fetchOwnerHistory } from "../api";
import { OwnerStatusForm } from "./owner-status-form";

const number = new Intl.NumberFormat("pt-PT");

type OwnerDetailsProps = {
  ownerId: string;
};

type Notice = { kind: "success" | "error"; text: string };

/**
 * US29: one owner for the admin: the numbers of the list, the status, the button to deactivate or reactivate (a form with the
 * reason, which says first how many future bookings stay confirmed) and the history of changes, newest first, with the date
 * in Lisbon, what happened, the reason and which admin did it. A 404 (an id that is not an owner's) says so; the owner's
 * emails and names are the only personal data here, and players only exist as counts.
 */
export function OwnerDetails({ ownerId }: OwnerDetailsProps) {
  const router = useRouter();
  const pathname = usePathname();
  const [formOpen, setFormOpen] = useState(false);
  // Cancelling the form puts focus back on the button that opened it (UX-002); a result takes focus itself.
  const statusButton = useRestoreFocus<HTMLButtonElement>(formOpen);
  const [notice, setNotice] = useState<Notice | null>(null);

  const owner = useQuery({ queryKey: adminOwnerQueryKey(ownerId), queryFn: () => fetchAdminOwner(ownerId) });
  useDocumentTitle("Dono", owner.data?.name);
  const history = useQuery({ queryKey: adminOwnerHistoryQueryKey(ownerId), queryFn: () => fetchOwnerHistory(ownerId), enabled: owner.isSuccess });

  // The session ended and could not be renewed: sign in again and come back here.
  const sessionEnded = [owner.error, history.error].some((error) => error instanceof ApiError && error.status === 401);
  useEffect(() => {
    if (sessionEnded) {
      router.replace(loginUrl(pathname));
    }
  }, [sessionEnded, router, pathname]);

  const back = (
    <Link href="/admin" className="-my-2 inline-flex min-h-11 w-fit items-center text-sm underline-offset-4 hover:underline sm:min-h-0 sm:py-0">
      ← Donos
    </Link>
  );

  if (owner.isPending || sessionEnded) {
    return (
      <LoadingRegion label="A carregar o dono…" className="flex flex-col gap-6">
        <Skeleton className="h-7 w-56" />
        <Skeleton className="h-32 w-full" />
        <Skeleton className="h-11 w-48" />
      </LoadingRegion>
    );
  }
  if (owner.isError) {
    if (owner.error instanceof ApiError && owner.error.code === "owner_not_found") {
      return <OwnerNotFound />;
    }
    return (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{errorMessage(owner.error instanceof ApiError ? owner.error.code : "unknown_error")}</p>
        <Button type="button" variant="outline" onClick={() => void owner.refetch()}>
          Tentar novamente
        </Button>
      </div>
    );
  }

  const data = owner.data;
  return (
    <div className="flex flex-col gap-8">
      {back}

      <section aria-labelledby="owner-summary" className="flex flex-col gap-4">
        <div className="flex flex-wrap items-center gap-3">
          <h2 id="owner-summary" className="text-xl font-semibold">
            {data.name}
          </h2>
          {data.deactivated ? (
            <span className="rounded-full border border-destructive/40 bg-destructive/10 px-2 py-0.5 text-xs text-destructive">Desativado</span>
          ) : (
            <span className="rounded-full border px-2 py-0.5 text-xs text-muted-foreground">Ativo</span>
          )}
        </div>
        <dl className="grid grid-cols-2 gap-x-6 gap-y-3 text-sm sm:grid-cols-3">
          <Fact label="Email" value={data.email} />
          <Fact label="Registo" value={formatLisbonDate(data.registeredAt)} />
          <Fact label="Clubes" value={number.format(data.clubs)} />
          <Fact label="Campos ativos" value={number.format(data.pitches)} />
          <Fact label="Reservas" value={number.format(data.bookings)} />
          <Fact label="Jogadores" value={number.format(data.players)} />
          <Fact label="Reservas futuras confirmadas" value={number.format(data.futureBookings)} />
        </dl>
      </section>

      {/* The result of the change takes focus where the form was (UX-002). */}
      {notice ? <ResultNotice kind={notice.kind}>{notice.text}</ResultNotice> : null}

      <section aria-labelledby="owner-status" className="flex flex-col gap-3">
        <h2 id="owner-status" className="text-lg font-semibold">
          Estado da conta
        </h2>
        {formOpen ? (
          <OwnerStatusForm
            key={data.deactivated ? "reactivate" : "deactivate"}
            owner={data}
            onCancel={() => setFormOpen(false)}
            onChanged={(changed) => {
              setFormOpen(false);
              setNotice({ kind: "success", text: changed.deactivated ? "Dono desativado." : "Dono reativado." });
            }}
            onRefused={(message) => {
              setFormOpen(false);
              setNotice({ kind: "error", text: message });
            }}
          />
        ) : (
          <div>
            <Button
              ref={statusButton}
              type="button"
              size="cta"
              variant={data.deactivated ? "default" : "destructive"}
              onClick={() => {
                setNotice(null);
                setFormOpen(true);
              }}
            >
              {data.deactivated ? "Reativar dono" : "Desativar dono"}
            </Button>
          </div>
        )}
      </section>

      <section aria-labelledby="owner-history" className="flex flex-col gap-3">
        <h2 id="owner-history" className="text-lg font-semibold">
          Histórico
        </h2>
        {history.isError ? (
          <div role="alert" className="flex flex-col items-start gap-3">
            <p>{errorMessage(history.error instanceof ApiError ? history.error.code : "unknown_error")}</p>
            <Button type="button" variant="outline" onClick={() => void history.refetch()}>
              Tentar novamente
            </Button>
          </div>
        ) : history.isPending ? (
          <LoadingRegion label="A carregar o histórico…">
            <Skeleton className="h-20 w-full" />
          </LoadingRegion>
        ) : history.data.length === 0 ? (
          <EmptyState title="Este dono nunca foi desativado." />
        ) : (
          <ol aria-label="Histórico de estados" className="flex flex-col gap-3">
            {history.data.map((change) => (
              <li key={change.id} className="flex flex-col gap-1 rounded-lg border p-4">
                <div className="flex flex-wrap items-baseline justify-between gap-2">
                  <p className="font-medium">{change.action === "deactivated" ? "Desativado" : "Reativado"}</p>
                  <p className="text-sm text-muted-foreground">{formatLisbonDateTime(change.at)}</p>
                </div>
                <p className="text-sm">{change.reason}</p>
                <p className="text-sm text-muted-foreground">Por {change.adminName}</p>
              </li>
            ))}
          </ol>
        )}
      </section>
    </div>
  );
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="font-medium break-words">{value}</dd>
    </div>
  );
}

/** An id that is not an owner's, or a malformed one (the page's own 404 shows this too). */
export function OwnerNotFound() {
  return (
    <div role="alert" className="flex flex-col items-start gap-3">
      <p>{errorMessage("owner_not_found")}</p>
      <Link href="/admin" className="text-sm underline underline-offset-4">
        Voltar aos donos
      </Link>
    </div>
  );
}
