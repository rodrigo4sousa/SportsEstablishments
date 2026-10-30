"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { FormField } from "@/components/form-field";
import { Button } from "@/components/ui/button";
import { errorMessage } from "@/lib/api/error-messages";
import { isExpectedApiError, unexpectedErrorMessage } from "@/lib/api/expected-error";
import { showApiFieldErrors } from "@/lib/api/form-errors";
import { adminOwnerHistoryQueryKey, adminOwnerQueryKey, adminQueryKey, changeOwnerStatus } from "../api";
import { OWNER_STATUS_REASON_MAX_LENGTH, ownerStatusSchema, type OwnerStatusData, type OwnerStatusValues } from "../schemas/owner-status";
import type { AdminOwnerDetail } from "../types";

/** The codes that mean the owner is not in the state the admin thought: say so, and load the owner again. */
const STALE_CODES = new Set(["owner_already_deactivated", "owner_not_deactivated", "owner_not_found"]);

type OwnerStatusFormProps = {
  owner: AdminOwnerDetail;
  /** Called with the owner as the API now has them, once the change is done. */
  onChanged: (owner: AdminOwnerDetail) => void;
  onCancel: () => void;
  /** Called with the message when the API says the owner is not what the admin thought. */
  onRefused: (message: string) => void;
};

/**
 * US29: deactivating an active owner, or reactivating a deactivated one, with a reason (required, up to 500 characters). Before
 * deactivating, it says how many future bookings of the owner's clubs stay confirmed (a deactivation cancels none), so the
 * admin knows what they are confirming. After the change the owner, the history, the list and the totals are loaded again.
 */
export function OwnerStatusForm({ owner, onChanged, onCancel, onRefused }: OwnerStatusFormProps) {
  const queryClient = useQueryClient();
  const deactivating = !owner.deactivated;
  const form = useForm<OwnerStatusValues, unknown, OwnerStatusData>({ resolver: zodResolver(ownerStatusSchema), defaultValues: { reason: "" } });
  const { errors } = form.formState;

  // UX-002: the form replaces the button that opened it, so focus goes to its field.
  const { setFocus } = form;
  useEffect(() => {
    setFocus("reason");
  }, [setFocus]);

  const change = useMutation({
    mutationFn: (reason: string) => changeOwnerStatus(owner.id, deactivating ? "deactivate" : "reactivate", reason),
    onSuccess: async (changed) => {
      queryClient.setQueryData(adminOwnerQueryKey(owner.id), changed);
      // The owner's page, the list of owners, the totals and the chart all depend on who is active.
      await queryClient.invalidateQueries({ queryKey: adminQueryKey, refetchType: "all" });
      onChanged(changed);
    },
    onError: async (error) => {
      if (!isExpectedApiError(error)) {
        toast.error(unexpectedErrorMessage(error));
        return;
      }
      if (error.code === "validation_failed" && showApiFieldErrors(form, error.fieldErrors, ["reason"])) {
        return;
      }
      if (STALE_CODES.has(error.code)) {
        onRefused(errorMessage(error.code));
        await queryClient.invalidateQueries({ queryKey: adminOwnerQueryKey(owner.id), refetchType: "all" });
        await queryClient.invalidateQueries({ queryKey: adminOwnerHistoryQueryKey(owner.id), refetchType: "all" });
        return;
      }
      toast.error(errorMessage(error.code));
    },
  });

  const reasonId = `owner-status-reason-${owner.id}`;
  const label = deactivating ? "Desativar dono" : "Reativar dono";

  return (
    <form
      noValidate
      aria-label={label}
      className={
        deactivating
          ? "flex flex-col gap-4 rounded-md border border-destructive/40 bg-destructive/5 p-4"
          : "flex flex-col gap-4 rounded-md border p-4"
      }
      onSubmit={form.handleSubmit((values) => {
        change.mutate(values.reason);
      })}
    >
      {deactivating ? (
        <div role="note" className="flex flex-col gap-1 text-sm">
          <p>
            O dono deixa de poder entrar, e os clubes dele deixam de aparecer na pesquisa e de aceitar reservas. A sessão que já tiver aberta
            pode durar até 15 minutos.
          </p>
          <p className="font-medium">
            {owner.futureBookings === 0
              ? "Não há reservas futuras nos clubes deste dono."
              : owner.futureBookings === 1
                ? "1 reserva futura continua confirmada: não é cancelada."
                : `${owner.futureBookings} reservas futuras continuam confirmadas: não são canceladas.`}
          </p>
        </div>
      ) : (
        <p role="note" className="text-sm">
          O dono volta a poder entrar (com uma sessão nova) e os clubes dele voltam à pesquisa e às reservas.
        </p>
      )}

      <FormField
        id={reasonId}
        label={deactivating ? "Motivo da desativação" : "Motivo da reativação"}
        hint={`Obrigatório, até ${OWNER_STATUS_REASON_MAX_LENGTH} caracteres. Fica registado com o teu nome e não pode ser editado.`}
        error={errors.reason?.message}
      >
        <textarea
          id={reasonId}
          rows={3}
          aria-invalid={errors.reason ? true : undefined}
          aria-describedby={errors.reason ? `${reasonId}-error` : `${reasonId}-hint`}
          className="min-h-20 w-full rounded-md border border-input bg-transparent px-3 py-2 text-base shadow-xs placeholder:text-muted-foreground focus-visible:border-ring aria-invalid:border-destructive aria-invalid:ring-destructive/20 md:text-sm dark:bg-input/30"
          {...form.register("reason")}
        />
      </FormField>

      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" variant={deactivating ? "destructive" : "default"} disabled={change.isPending}>
          {change.isPending ? "A guardar…" : deactivating ? "Confirmar desativação" : "Confirmar reativação"}
        </Button>
        <Button type="button" variant="outline" disabled={change.isPending} onClick={onCancel}>
          Voltar
        </Button>
      </div>
    </form>
  );
}
