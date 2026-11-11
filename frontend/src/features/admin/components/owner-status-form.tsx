"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo } from "react";
import { useTranslations } from "next-intl";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { FormField } from "@/components/form-field";
import { Button } from "@/components/ui/button";
import { useApiErrors } from "@/lib/api/error-messages";
import { isExpectedApiError } from "@/lib/api/expected-error";
import { showApiFieldErrors, useApiFieldMessage } from "@/lib/api/form-errors";
import { adminOwnerHistoryQueryKey, adminOwnerQueryKey, adminQueryKey, changeOwnerStatus } from "../api";
import { OWNER_STATUS_REASON_MAX_LENGTH, createOwnerStatusSchema, type OwnerStatusData, type OwnerStatusValues } from "../schemas/owner-status";
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
  const t = useTranslations("admin.statusForm");
  const ownerT = useTranslations("admin.owner");
  const validation = useTranslations("admin.validation");
  const apiErrors = useApiErrors();
  const fieldMessage = useApiFieldMessage();
  const schema = useMemo(() => createOwnerStatusSchema(validation), [validation]);
  const form = useForm<OwnerStatusValues, unknown, OwnerStatusData>({ resolver: zodResolver(schema), defaultValues: { reason: "" } });
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
        toast.error(apiErrors.unexpected(error));
        return;
      }
      if (error.code === "validation_failed" && showApiFieldErrors(form, error.fieldErrors, ["reason"], fieldMessage)) {
        return;
      }
      if (STALE_CODES.has(error.code)) {
        onRefused(apiErrors.message(error.code));
        await queryClient.invalidateQueries({ queryKey: adminOwnerQueryKey(owner.id), refetchType: "all" });
        await queryClient.invalidateQueries({ queryKey: adminOwnerHistoryQueryKey(owner.id), refetchType: "all" });
        return;
      }
      toast.error(apiErrors.message(error.code));
    },
  });

  const reasonId = `owner-status-reason-${owner.id}`;
  const label = deactivating ? ownerT("deactivate") : ownerT("reactivate");

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
          <p>{t("deactivateNote")}</p>
          <p className="font-medium">{t("futureBookings", { count: owner.futureBookings })}</p>
        </div>
      ) : (
        <p role="note" className="text-sm">
          {t("reactivateNote")}
        </p>
      )}

      <FormField
        id={reasonId}
        label={deactivating ? t("deactivationReason") : t("reactivationReason")}
        hint={t("reasonHint", { max: OWNER_STATUS_REASON_MAX_LENGTH })}
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
          {change.isPending ? t("saving") : deactivating ? t("confirmDeactivation") : t("confirmReactivation")}
        </Button>
        <Button type="button" variant="outline" disabled={change.isPending} onClick={onCancel}>
          {t("back")}
        </Button>
      </div>
    </form>
  );
}
