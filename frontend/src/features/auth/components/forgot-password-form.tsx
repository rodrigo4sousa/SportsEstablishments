"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation } from "@tanstack/react-query";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { useMemo } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { RateLimitMessage } from "@/components/rate-limit-message";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { NETWORK_ERROR } from "@/lib/api/client";
import { useApiErrors } from "@/lib/api/error-messages";
import { isExpectedApiError } from "@/lib/api/expected-error";
import { useApiFieldMessage } from "@/lib/api/form-errors";
import { useRetryAfter } from "@/lib/api/use-retry-after";
import { requestPasswordReset } from "../api";
import { createForgotPasswordSchema, type ForgotPasswordData, type ForgotPasswordValues } from "../schemas/forgot-password";

/** Failures shown inside the form, chosen by the API's `code`. Anything else is unexpected and goes to a toast. */
const FORM_LEVEL_CODES = new Set(["too_many_requests", NETWORK_ERROR]);

/**
 * US05: asks for the reset email. Whatever the email, an accepted request shows the one message above; the form never says whether
 * an account exists. Only a malformed email (or too many requests) is answered differently.
 */
export function ForgotPasswordForm() {
  const t = useTranslations("auth");
  const validation = useTranslations("validation");
  const apiErrors = useApiErrors();
  const fieldMessage = useApiFieldMessage();
  const schema = useMemo(() => createForgotPasswordSchema(validation), [validation]);
  const form = useForm<ForgotPasswordValues, unknown, ForgotPasswordData>({
    resolver: zodResolver(schema),
    defaultValues: { email: "" },
  });
  const { errors } = form.formState;
  const retry = useRetryAfter();

  const ask = useMutation({
    mutationFn: (values: ForgotPasswordData) => requestPasswordReset(values.email),
    onError: (error) => {
      if (!isExpectedApiError(error, { handlesNetworkError: true })) {
        toast.error(apiErrors.unexpected(error));
        return;
      }
      if (error.code === "too_many_requests" && error.retryAfterSeconds !== undefined) {
        // UX-018: it says how long to wait, so say it, count it down, and keep the button off until then.
        form.clearErrors("root");
        retry.start(error.retryAfterSeconds);
        return;
      }
      if (error.code === "validation_failed" && error.fieldErrors.email?.length) {
        form.setError("email", { message: fieldMessage("email") });
        return;
      }
      if (FORM_LEVEL_CODES.has(error.code)) {
        form.setError("root", { message: apiErrors.message(error.code) });
        return;
      }
      toast.error(apiErrors.message(error.code));
    },
  });

  if (ask.isSuccess) {
    return (
      <div className="flex flex-col gap-4">
        <p role="status" className="rounded-md border border-emerald-600/40 bg-emerald-50 px-3 py-2 text-sm dark:bg-emerald-950/30">
          {t("forgot.sent")}
        </p>
        <p className="text-sm text-muted-foreground">
          {t.rich("forgot.notArrived", {
            again: (chunks) => (
              <button
                type="button"
                className="underline underline-offset-4"
                onClick={() => {
                  form.reset({ email: "" });
                  ask.reset();
                }}
              >
                {chunks}
              </button>
            ),
          })}
        </p>
        <p className="text-sm text-muted-foreground">
          <Link href="/login" className="underline underline-offset-4">
            {t("forgot.backToLogin")}
          </Link>
        </p>
      </div>
    );
  }

  return (
    <form noValidate aria-label={t("forgot.form")} className="flex flex-col gap-5" onSubmit={form.handleSubmit((values) => {
        retry.clear();
        ask.mutate(values);
      })}>
      {errors.root?.message ? (
        <p role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive">
          {errors.root.message}
        </p>
      ) : null}

      <div className="flex flex-col gap-2">
        <Label htmlFor="forgot-email">{t("email")}</Label>
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

      <RateLimitMessage retry={retry} />

      <Button type="submit" disabled={ask.isPending || retry.waiting}>
        {ask.isPending ? t("forgot.submitting") : t("forgot.submit")}
      </Button>
      <p className="text-sm text-muted-foreground">
        <Link href="/login" className="underline underline-offset-4">
          {t("forgot.backToLogin")}
        </Link>
      </p>
    </form>
  );
}
