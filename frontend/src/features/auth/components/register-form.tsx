"use client";

import type { ReactNode } from "react";
import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { currentUserQueryKey, registerPlayer } from "../api";
import {
  PASSWORD_MIN_LENGTH,
  registerSchema,
  type RegisterFormData,
  type RegisterFormValues,
} from "../schemas/register";

const FIELDS = ["name", "email", "password", "phone"] as const satisfies ReadonlyArray<keyof RegisterFormValues>;

type FieldName = (typeof FIELDS)[number];

const EMPTY_FORM: RegisterFormValues = { name: "", email: "", password: "", phone: "" };

/** US01: player registration. On success the player is signed in and goes to the home page. */
export function RegisterForm() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const form = useForm<RegisterFormValues, unknown, RegisterFormData>({
    resolver: zodResolver(registerSchema),
    defaultValues: EMPTY_FORM,
  });
  const { errors } = form.formState;

  const registration = useMutation({
    mutationFn: registerPlayer,
    onSuccess: (account) => {
      queryClient.setQueryData(currentUserQueryKey, account);
      // Pages stay mounted across navigations (cacheComponents), so clear the form, password included.
      form.reset(EMPTY_FORM);
      router.replace("/");
    },
    onError: (error) => {
      if (!(error instanceof ApiError)) {
        toast.error(errorMessage("unknown_error"));
        return;
      }
      if (error.code === "email_taken") {
        form.setError("email", { message: errorMessage(error.code) }, { shouldFocus: true });
        return;
      }
      if (error.code === "validation_failed" && showFieldErrors(error.fieldErrors)) {
        return;
      }
      toast.error(errorMessage(error.code));
    },
  });

  // Puts the API's per-field messages on their fields; false when none of them is a field of this form.
  function showFieldErrors(fieldErrors: Readonly<Record<string, string[]>>): boolean {
    const fields = FIELDS.filter((field) => fieldErrors[field]?.length);
    fields.forEach((field, index) =>
      form.setError(field, { message: fieldErrors[field].join(" ") }, { shouldFocus: index === 0 }),
    );
    return fields.length > 0;
  }

  const describedBy = (field: FieldName, hint?: boolean) =>
    [hint ? `register-${field}-hint` : null, errors[field] ? `register-${field}-error` : null].filter(Boolean).join(" ") ||
    undefined;

  return (
    <form
      noValidate
      aria-label="Criar conta"
      className="flex flex-col gap-5"
      onSubmit={form.handleSubmit((values) => registration.mutate(values))}
    >
      <Field id="register-name" label="Nome" error={errors.name?.message}>
        <Input
          id="register-name"
          autoComplete="name"
          aria-invalid={errors.name ? true : undefined}
          aria-describedby={describedBy("name")}
          {...form.register("name")}
        />
      </Field>

      <Field id="register-email" label="Email" error={errors.email?.message}>
        <Input
          id="register-email"
          type="email"
          autoComplete="email"
          inputMode="email"
          aria-invalid={errors.email ? true : undefined}
          aria-describedby={describedBy("email")}
          {...form.register("email")}
        />
      </Field>

      <Field
        id="register-password"
        label="Palavra-passe"
        hint={`Pelo menos ${PASSWORD_MIN_LENGTH} caracteres.`}
        error={errors.password?.message}
      >
        <Input
          id="register-password"
          type="password"
          autoComplete="new-password"
          aria-invalid={errors.password ? true : undefined}
          aria-describedby={describedBy("password", true)}
          {...form.register("password")}
        />
      </Field>

      <Field id="register-phone" label="Telemóvel (opcional)" error={errors.phone?.message}>
        <Input
          id="register-phone"
          type="tel"
          autoComplete="tel"
          inputMode="tel"
          aria-invalid={errors.phone ? true : undefined}
          aria-describedby={describedBy("phone")}
          {...form.register("phone")}
        />
      </Field>

      <Button type="submit" disabled={registration.isPending}>
        {registration.isPending ? "A criar conta…" : "Criar conta"}
      </Button>
    </form>
  );
}

type FieldProps = {
  id: string;
  label: string;
  hint?: string;
  error?: string;
  children: ReactNode;
};

function Field({ id, label, hint, error, children }: FieldProps) {
  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id}>{label}</Label>
      {children}
      {hint ? (
        <p id={`${id}-hint`} className="text-sm text-muted-foreground">
          {hint}
        </p>
      ) : null}
      {error ? (
        <p id={`${id}-error`} className="text-sm text-destructive">
          {error}
        </p>
      ) : null}
    </div>
  );
}
