"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { FormField } from "@/components/form-field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { errorMessage } from "@/lib/api/error-messages";
import { isExpectedApiError, unexpectedErrorMessage } from "@/lib/api/expected-error";
import { showApiFieldErrors } from "@/lib/api/form-errors";
import { clubQueryKey, clubsQueryKey, createClub, updateClub, type ClubRequest, type OwnerClub } from "../api";
import {
  CLUB_ADDRESS_MAX_LENGTH,
  CLUB_LOCALITY_MAX_LENGTH,
  CLUB_NAME_MAX_LENGTH,
  DEFAULT_CANCELLATION_WINDOW_HOURS,
  MAX_CANCELLATION_WINDOW_HOURS,
  MIN_CANCELLATION_WINDOW_HOURS,
  clubSchema,
  type ClubFormData,
  type ClubFormValues,
} from "../schemas/club";

const FIELDS = ["name", "address", "locality", "phone", "contactEmail", "cancellationWindowHours"] as const satisfies ReadonlyArray<
  keyof ClubFormValues
>;

type FieldName = (typeof FIELDS)[number];

const NEW_CLUB: ClubFormValues = {
  name: "",
  address: "",
  locality: "",
  phone: "",
  contactEmail: "",
  cancellationWindowHours: String(DEFAULT_CANCELLATION_WINDOW_HOURS),
  notifyOnNewBooking: true,
};

function valuesOf(club: OwnerClub): ClubFormValues {
  return {
    name: club.name,
    address: club.address,
    locality: club.locality,
    phone: club.phone,
    contactEmail: club.contactEmail,
    cancellationWindowHours: String(club.cancellationWindowHours),
    notifyOnNewBooking: club.notifyOnNewBooking,
  };
}

type ClubFormProps = {
  /** The club being edited; without it the form creates a new one. */
  club?: OwnerClub;
};

/**
 * US06: create or edit a club. Creating goes on to the new club's page; editing stays here. The slug and the time
 * zone are not part of the form (the slug never changes, the time zone is always Europe/Lisbon).
 */
export function ClubForm({ club }: ClubFormProps) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const form = useForm<ClubFormValues, unknown, ClubFormData>({
    resolver: zodResolver(clubSchema),
    defaultValues: club ? valuesOf(club) : NEW_CLUB,
  });
  const { errors } = form.formState;

  const save = useMutation({
    mutationFn: (request: ClubRequest) => (club ? updateClub(club.id, request) : createClub(request)),
    onSuccess: async (saved) => {
      queryClient.setQueryData(clubQueryKey(saved.id), saved);
      // The lists (names, order, pages) may have changed.
      await queryClient.invalidateQueries({ queryKey: clubsQueryKey, refetchType: "all" });
      if (club) {
        form.reset(valuesOf(saved));
        toast.success("Alterações guardadas.");
        return;
      }
      toast.success("Clube criado.");
      router.replace(`/manage/clubs/${saved.id}`);
    },
    onError: (error) => {
      if (!isExpectedApiError(error)) {
        toast.error(unexpectedErrorMessage(error));
        return;
      }
      if (error.code === "validation_failed" && showApiFieldErrors(form, error.fieldErrors, FIELDS)) {
        return;
      }
      toast.error(errorMessage(error.code));
    },
  });

  const describedBy = (field: FieldName, hint?: boolean) =>
    [hint ? `club-${field}-hint` : null, errors[field] ? `club-${field}-error` : null].filter(Boolean).join(" ") || undefined;
  const invalid = (field: FieldName) => (errors[field] ? true : undefined);

  return (
    <form
      noValidate
      aria-label={club ? "Editar clube" : "Criar clube"}
      className="flex flex-col gap-5"
      onSubmit={form.handleSubmit((values) => save.mutate(values))}
    >
      <FormField id="club-name" label="Nome" error={errors.name?.message} hint={club ? undefined : `Até ${CLUB_NAME_MAX_LENGTH} caracteres.`}>
        <Input id="club-name" autoComplete="organization" aria-invalid={invalid("name")} aria-describedby={describedBy("name", !club)} {...form.register("name")} />
      </FormField>

      <FormField id="club-address" label="Morada" error={errors.address?.message}>
        <Input
          id="club-address"
          autoComplete="street-address"
          maxLength={CLUB_ADDRESS_MAX_LENGTH * 2}
          aria-invalid={invalid("address")}
          aria-describedby={describedBy("address")}
          {...form.register("address")}
        />
      </FormField>

      <FormField id="club-locality" label="Localidade" error={errors.locality?.message}>
        <Input
          id="club-locality"
          autoComplete="address-level2"
          maxLength={CLUB_LOCALITY_MAX_LENGTH * 2}
          aria-invalid={invalid("locality")}
          aria-describedby={describedBy("locality")}
          {...form.register("locality")}
        />
      </FormField>

      <FormField id="club-phone" label="Telefone" error={errors.phone?.message}>
        <Input
          id="club-phone"
          type="tel"
          inputMode="tel"
          autoComplete="tel"
          aria-invalid={invalid("phone")}
          aria-describedby={describedBy("phone")}
          {...form.register("phone")}
        />
      </FormField>

      <FormField id="club-contactEmail" label="Email de contacto" error={errors.contactEmail?.message}>
        <Input
          id="club-contactEmail"
          type="email"
          inputMode="email"
          autoComplete="email"
          aria-invalid={invalid("contactEmail")}
          aria-describedby={describedBy("contactEmail")}
          {...form.register("contactEmail")}
        />
      </FormField>

      <FormField
        id="club-cancellationWindowHours"
        label="Janela de cancelamento (horas)"
        hint={`Quantas horas antes do início um jogador ainda pode cancelar (de ${MIN_CANCELLATION_WINDOW_HOURS} a ${MAX_CANCELLATION_WINDOW_HOURS}).`}
        error={errors.cancellationWindowHours?.message}
      >
        <Input
          id="club-cancellationWindowHours"
          inputMode="numeric"
          aria-invalid={invalid("cancellationWindowHours")}
          aria-describedby={describedBy("cancellationWindowHours", true)}
          {...form.register("cancellationWindowHours")}
        />
      </FormField>

      <div className="flex items-start gap-3">
        <input
          id="club-notifyOnNewBooking"
          type="checkbox"
          role="switch"
          className="mt-1 size-4"
          aria-describedby="club-notifyOnNewBooking-hint"
          {...form.register("notifyOnNewBooking")}
        />
        <div className="flex flex-col gap-1">
          <Label htmlFor="club-notifyOnNewBooking">Receber um email por cada nova reserva</Label>
          <p id="club-notifyOnNewBooking-hint" className="text-sm text-muted-foreground">
            Só para as reservas que os jogadores fazem; as que fazes tu para clientes não enviam email.
          </p>
        </div>
      </div>

      {club ? (
        <p className="text-sm text-muted-foreground">
          Endereço público: <span className="font-mono">/clubs/{club.slug}</span>. Não muda se mudares o nome.
        </p>
      ) : null}

      <div className="flex items-center gap-3">
        <Button type="submit" disabled={save.isPending}>
          {save.isPending ? "A guardar…" : club ? "Guardar alterações" : "Criar clube"}
        </Button>
        <Button asChild variant="ghost">
          <Link href="/manage">Voltar aos clubes</Link>
        </Button>
      </div>
    </form>
  );
}
