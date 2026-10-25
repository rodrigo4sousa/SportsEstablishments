"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation } from "@tanstack/react-query";
import Link from "next/link";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError, NETWORK_ERROR } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { requestPasswordReset } from "../api";
import { forgotPasswordSchema, type ForgotPasswordData, type ForgotPasswordValues } from "../schemas/forgot-password";

/** What every request is answered with, whether or not an account has the email (US05). */
export const FORGOT_PASSWORD_MESSAGE =
  "Se existir uma conta com este email, vais receber uma ligação para redefinir a palavra-passe. A ligação é válida durante 1 hora.";

/** Failures shown inside the form, chosen by the API's `code`. Anything else is unexpected and goes to a toast. */
const FORM_LEVEL_CODES = new Set(["too_many_requests", NETWORK_ERROR]);

/**
 * US05: asks for the reset email. Whatever the email, an accepted request shows the one message above; the form never says whether
 * an account exists. Only a malformed email (or too many requests) is answered differently.
 */
export function ForgotPasswordForm() {
  const form = useForm<ForgotPasswordValues, unknown, ForgotPasswordData>({
    resolver: zodResolver(forgotPasswordSchema),
    defaultValues: { email: "" },
  });
  const { errors } = form.formState;

  const ask = useMutation({
    mutationFn: (values: ForgotPasswordData) => requestPasswordReset(values.email),
    onError: (error) => {
      if (!(error instanceof ApiError)) {
        toast.error(errorMessage("unknown_error"));
        return;
      }
      if (error.code === "validation_failed" && error.fieldErrors.email?.length) {
        form.setError("email", { message: error.fieldErrors.email.join(" ") });
        return;
      }
      if (FORM_LEVEL_CODES.has(error.code)) {
        form.setError("root", { message: errorMessage(error.code) });
        return;
      }
      toast.error(errorMessage(error.code));
    },
  });

  if (ask.isSuccess) {
    return (
      <div className="flex flex-col gap-4">
        <p role="status" className="rounded-md border border-emerald-600/40 bg-emerald-50 px-3 py-2 text-sm dark:bg-emerald-950/30">
          {FORGOT_PASSWORD_MESSAGE}
        </p>
        <p className="text-sm text-muted-foreground">
          Não chegou? Vê a pasta de spam e, passados uns minutos,{" "}
          <button
            type="button"
            className="underline underline-offset-4"
            onClick={() => {
              form.reset({ email: "" });
              ask.reset();
            }}
          >
            pede outra ligação
          </button>
          .
        </p>
        <p className="text-sm text-muted-foreground">
          <Link href="/login" className="underline underline-offset-4">
            Voltar a entrar
          </Link>
        </p>
      </div>
    );
  }

  return (
    <form noValidate aria-label="Recuperar palavra-passe" className="flex flex-col gap-5" onSubmit={form.handleSubmit((values) => ask.mutate(values))}>
      {errors.root?.message ? (
        <p role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive">
          {errors.root.message}
        </p>
      ) : null}

      <div className="flex flex-col gap-2">
        <Label htmlFor="forgot-email">Email</Label>
        <Input
          id="forgot-email"
          type="email"
          autoComplete="email"
          inputMode="email"
          aria-invalid={errors.email ? true : undefined}
          aria-describedby={errors.email ? "forgot-email-error" : undefined}
          {...form.register("email")}
        />
        {errors.email?.message ? (
          <p id="forgot-email-error" className="text-sm text-destructive">
            {errors.email.message}
          </p>
        ) : null}
      </div>

      <Button type="submit" disabled={ask.isPending}>
        {ask.isPending ? "A enviar…" : "Enviar ligação"}
      </Button>
      <p className="text-sm text-muted-foreground">
        <Link href="/login" className="underline underline-offset-4">
          Voltar a entrar
        </Link>
      </p>
    </form>
  );
}
