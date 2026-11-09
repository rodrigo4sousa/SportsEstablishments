"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { RateLimitMessage } from "@/components/rate-limit-message";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { FormField } from "@/components/form-field";
import { errorMessage } from "@/lib/api/error-messages";
import { isExpectedApiError, unexpectedErrorMessage } from "@/lib/api/expected-error";
import { useRetryAfter } from "@/lib/api/use-retry-after";
import { showApiFieldErrors } from "@/lib/api/form-errors";
import { safeNext } from "@/lib/auth/redirects";
import { LEGAL_VERSION, PRIVACY_PATH, TERMS_PATH } from "@/features/legal/legal-info";
import { currentUserQueryKey, registerAccount, type RegisterAccountRequest, type RegistrationVariant } from "../api";
import {
  PASSWORD_MIN_LENGTH,
  registerSchema,
  type RegisterFormData,
  type RegisterFormValues,
} from "../schemas/register";

const FIELDS = ["name", "email", "password", "phone"] as const satisfies ReadonlyArray<keyof RegisterFormValues>;

type FieldName = (typeof FIELDS)[number];

const EMPTY_FORM: RegisterFormValues = { name: "", email: "", password: "", phone: "", acceptTerms: false };

type RegisterFormProps = {
  /** Player (US01) or owner (US02) sign-up: the same form, a different endpoint. Defaults to player. */
  variant?: RegistrationVariant;
  /** The page to go to after signing up; only used if it belongs to the new account's area (see `safeNext`). */
  next?: string | null;
};

const FORM_LABELS: Record<RegistrationVariant, string> = {
  player: "Criar conta",
  owner: "Criar conta de dono",
};

/**
 * US01 and US02 registration. On success the user is signed in and goes to `next` if it is a page of their area (a
 * player who was sent from a club page returns to it), otherwise to the home of their profile: `/` for a player,
 * `/manage` for an owner.
 */
export function RegisterForm({ variant = "player", next = null }: RegisterFormProps) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const form = useForm<RegisterFormValues, unknown, RegisterFormData>({
    resolver: zodResolver(registerSchema),
    defaultValues: EMPTY_FORM,
  });
  const { errors } = form.formState;
  const retry = useRetryAfter();

  const registration = useMutation({
    mutationFn: (request: RegisterAccountRequest) => registerAccount(variant, request),
    onSuccess: (account) => {
      queryClient.setQueryData(currentUserQueryKey, account);
      // Pages stay mounted across navigations (cacheComponents), so clear the form, password included.
      form.reset(EMPTY_FORM);
      router.replace(safeNext(next, account.profile));
    },
    onError: (error) => {
      if (!isExpectedApiError(error)) {
        toast.error(unexpectedErrorMessage(error));
        return;
      }
      if (error.code === "too_many_requests" && error.retryAfterSeconds !== undefined) {
        // UX-018: it says how long to wait, so say it, count it down, and keep the button off until then.
        form.clearErrors("root");
        retry.start(error.retryAfterSeconds);
        return;
      }
      if (error.code === "email_taken") {
        form.setError("email", { message: errorMessage(error.code) }, { shouldFocus: true });
        return;
      }
      if (error.code === "terms_not_accepted") {
        form.setError("acceptTerms", { message: errorMessage(error.code) });
        // `shouldFocus` above does nothing for a checkbox in React Hook Form; `setFocus` does.
        form.setFocus("acceptTerms");
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
      onSubmit={form.handleSubmit((values) => {
        retry.clear();
        registration.mutate({
          name: values.name,
          email: values.email,
          password: values.password,
          phone: values.phone,
          acceptedTermsVersion: LEGAL_VERSION,
        });
      })}
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

      <div className="flex flex-col gap-2">
        <div className="flex items-start gap-3">
          <input
            id="register-acceptTerms"
            type="checkbox"
            required
            className="mt-0.5 size-4 shrink-0"
            aria-invalid={errors.acceptTerms ? true : undefined}
            aria-describedby={`register-acceptTerms-hint${errors.acceptTerms ? " register-acceptTerms-error" : ""}`}
            {...form.register("acceptTerms")}
          />
          <Label htmlFor="register-acceptTerms" className="block leading-snug font-normal">
            Li e aceito os{" "}
            <Link href={TERMS_PATH} target="_blank" rel="noopener" className="underline underline-offset-4">
              Termos e Condições
            </Link>{" "}
            e tomei conhecimento da{" "}
            <Link href={PRIVACY_PATH} target="_blank" rel="noopener" className="underline underline-offset-4">
              Política de Privacidade
            </Link>
          </Label>
        </div>
        <p id="register-acceptTerms-hint" className="text-sm text-muted-foreground">
          As ligações abrem num novo separador.
        </p>
        {errors.acceptTerms ? (
          <p id="register-acceptTerms-error" className="text-sm text-destructive">
            {errors.acceptTerms.message}
          </p>
        ) : null}
      </div>

      <RateLimitMessage retry={retry} />

      <Button type="submit" disabled={registration.isPending || retry.waiting}>
        {registration.isPending ? "A criar conta…" : "Criar conta"}
      </Button>
    </form>
  );
}
