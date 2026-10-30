"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { RateLimitMessage } from "@/components/rate-limit-message";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NETWORK_ERROR } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { isExpectedApiError, unexpectedErrorMessage } from "@/lib/api/expected-error";
import { useRetryAfter } from "@/lib/api/use-retry-after";
import { safeNext } from "@/lib/auth/redirects";
import { currentUserQueryKey, login } from "../api";
import { loginSchema, type LoginFormData, type LoginFormValues } from "../schemas/login";

const EMPTY_FORM: LoginFormValues = { email: "", password: "" };

/** Failures shown inside the form, chosen by the API's `code`. Anything else is unexpected and goes to a toast. */
const FORM_LEVEL_CODES = new Set(["invalid_credentials", "too_many_requests", NETWORK_ERROR]);

type LoginFormProps = {
  /** The page to go back to after signing in; only used if it belongs to the user's area (see `safeNext`). */
  next: string | null;
};

/** US03: sign-in for every profile. After success, the user goes to the area of their profile. */
export function LoginForm({ next }: LoginFormProps) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const form = useForm<LoginFormValues, unknown, LoginFormData>({
    resolver: zodResolver(loginSchema),
    defaultValues: EMPTY_FORM,
  });
  const { errors } = form.formState;
  const retry = useRetryAfter();

  const signIn = useMutation({
    mutationFn: login,
    onSuccess: (account) => {
      queryClient.setQueryData(currentUserQueryKey, account);
      // Pages stay mounted across navigations (cacheComponents), so clear the form, password included.
      form.reset(EMPTY_FORM);
      router.replace(safeNext(next, account.profile));
    },
    onError: (error) => {
      if (!isExpectedApiError(error, { handlesNetworkError: true })) {
        toast.error(unexpectedErrorMessage(error));
        return;
      }
      if (error.code === "too_many_requests" && error.retryAfterSeconds !== undefined) {
        // UX-018: it says how long to wait, so say it, count it down, and keep the button off until then.
        form.clearErrors("root");
        retry.start(error.retryAfterSeconds);
        return;
      }
      if (error.code === "validation_failed") {
        for (const field of ["email", "password"] as const) {
          const messages = error.fieldErrors[field];
          if (messages?.length) {
            form.setError(field, { message: messages.join(" ") });
          }
        }
        return;
      }
      if (FORM_LEVEL_CODES.has(error.code)) {
        form.resetField("password");
        // The submit button was disabled while the request ran and lost focus: send it to the field to try again (UX-002).
        form.setFocus("password");
        form.setError("root", { message: errorMessage(error.code) });
        return;
      }
      toast.error(errorMessage(error.code));
    },
  });

  const describedBy = (field: "email" | "password") => (errors[field] ? `login-${field}-error` : undefined);

  return (
    <form noValidate aria-label="Entrar" className="flex flex-col gap-5" onSubmit={form.handleSubmit((values) => {
        retry.clear();
        signIn.mutate(values);
      })}>
      {errors.root?.message ? (
        <p role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive">
          {errors.root.message}
        </p>
      ) : null}

      <div className="flex flex-col gap-2">
        <Label htmlFor="login-email">Email</Label>
        <Input
          id="login-email"
          type="email"
          autoComplete="email"
          inputMode="email"
          aria-invalid={errors.email ? true : undefined}
          aria-describedby={describedBy("email")}
          {...form.register("email")}
        />
        {errors.email?.message ? (
          <p id="login-email-error" className="text-sm text-destructive">
            {errors.email.message}
          </p>
        ) : null}
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor="login-password">Palavra-passe</Label>
        <Input
          id="login-password"
          type="password"
          autoComplete="current-password"
          aria-invalid={errors.password ? true : undefined}
          aria-describedby={describedBy("password")}
          {...form.register("password")}
        />
        {errors.password?.message ? (
          <p id="login-password-error" className="text-sm text-destructive">
            {errors.password.message}
          </p>
        ) : null}
      </div>

      <RateLimitMessage retry={retry} />

      <Button type="submit" disabled={signIn.isPending || retry.waiting}>
        {signIn.isPending ? "A entrar…" : "Entrar"}
      </Button>
    </form>
  );
}
