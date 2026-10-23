"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { FormField } from "@/components/form-field";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { showApiFieldErrors } from "@/lib/api/form-errors";
import { profileHome } from "@/lib/auth/redirects";
import { currentUserQueryKey, registerAccount, type RegisterAccountRequest, type RegistrationVariant } from "../api";
import {
  PASSWORD_MIN_LENGTH,
  registerSchema,
  type RegisterFormData,
  type RegisterFormValues,
} from "../schemas/register";

const FIELDS = ["name", "email", "password", "phone"] as const satisfies ReadonlyArray<keyof RegisterFormValues>;

type FieldName = (typeof FIELDS)[number];

const EMPTY_FORM: RegisterFormValues = { name: "", email: "", password: "", phone: "" };

type RegisterFormProps = {
  /** Player (US01) or owner (US02) sign-up: the same form, a different endpoint. Defaults to player. */
  variant?: RegistrationVariant;
};

const FORM_LABELS: Record<RegistrationVariant, string> = {
  player: "Criar conta",
  owner: "Criar conta de dono",
};

/**
 * US01 and US02 registration. On success the user is signed in and goes to the home of their profile:
 * `/` for a player, `/manage` for an owner.
 */
export function RegisterForm({ variant = "player" }: RegisterFormProps) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const form = useForm<RegisterFormValues, unknown, RegisterFormData>({
    resolver: zodResolver(registerSchema),
    defaultValues: EMPTY_FORM,
  });
  const { errors } = form.formState;

  const registration = useMutation({
    mutationFn: (request: RegisterAccountRequest) => registerAccount(variant, request),
    onSuccess: (account) => {
      queryClient.setQueryData(currentUserQueryKey, account);
      // Pages stay mounted across navigations (cacheComponents), so clear the form, password included.
      form.reset(EMPTY_FORM);
      router.replace(profileHome(account.profile));
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
      if (error.code === "validation_failed" && showApiFieldErrors(form, error.fieldErrors, FIELDS)) {
        return;
      }
      toast.error(errorMessage(error.code));
    },
  });

  const describedBy = (field: FieldName, hint?: boolean) =>
    [hint ? `register-${field}-hint` : null, errors[field] ? `register-${field}-error` : null].filter(Boolean).join(" ") ||
    undefined;

  return (
    <form
      noValidate
      aria-label={FORM_LABELS[variant]}
      className="flex flex-col gap-5"
      onSubmit={form.handleSubmit((values) => registration.mutate(values))}
    >
      <FormField id="register-name" label="Nome" error={errors.name?.message}>
        <Input
          id="register-name"
          autoComplete="name"
          aria-invalid={errors.name ? true : undefined}
          aria-describedby={describedBy("name")}
          {...form.register("name")}
        />
      </FormField>

      <FormField id="register-email" label="Email" error={errors.email?.message}>
        <Input
          id="register-email"
          type="email"
          autoComplete="email"
          inputMode="email"
          aria-invalid={errors.email ? true : undefined}
          aria-describedby={describedBy("email")}
          {...form.register("email")}
        />
      </FormField>

      <FormField
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
      </FormField>

      <FormField id="register-phone" label="Telemóvel (opcional)" error={errors.phone?.message}>
        <Input
          id="register-phone"
          type="tel"
          autoComplete="tel"
          inputMode="tel"
          aria-invalid={errors.phone ? true : undefined}
          aria-describedby={describedBy("phone")}
          {...form.register("phone")}
        />
      </FormField>

      <Button type="submit" disabled={registration.isPending}>
        {registration.isPending ? "A criar conta…" : "Criar conta"}
      </Button>
    </form>
  );
}
