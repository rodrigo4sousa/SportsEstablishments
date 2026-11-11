"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation } from "@tanstack/react-query";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { useMemo } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { RateLimitMessage } from "@/components/rate-limit-message";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError, NETWORK_ERROR } from "@/lib/api/client";
import { useApiErrors } from "@/lib/api/error-messages";
import { isExpectedApiError } from "@/lib/api/expected-error";
import { useApiFieldMessage } from "@/lib/api/form-errors";
import { useRetryAfter } from "@/lib/api/use-retry-after";
import { confirmPasswordReset } from "../api";
import { PASSWORD_MIN_LENGTH } from "../schemas/register";
import { createResetPasswordSchema, type ResetPasswordData, type ResetPasswordValues } from "../schemas/reset-password";

const EMPTY_FORM: ResetPasswordValues = { newPassword: "", confirmation: "" };

/** Failures shown inside the form, chosen by the API's `code`. Anything else is unexpected and goes to a toast. */
const FORM_LEVEL_CODES = new Set(["too_many_requests", NETWORK_ERROR]);

/** The bad link: said once, with the way to ask for a new one. The same for every reason a link can be bad. */
function InvalidLink() {
  const t = useTranslations("auth.reset");
  const apiErrors = useApiErrors();
  return (
    <div className="flex flex-col gap-4">
      <p role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive">
        {apiErrors.message("invalid_or_expired_reset_link")}
      </p>
      <p className="text-sm">
        <Link href="/forgot-password" className="underline underline-offset-4">
          {t("askNewLink")}
        </Link>
      </p>
    </div>
  );
}

/**
 * US05: the page of the emailed link (`?email=&token=`). The new password follows the rules of US01 and has to be typed twice. On
 * success the person is not signed in: a message and a link to the login page. A link that is wrong, expired or used (the API says
 * so with one code, whatever the reason) shows a message and a link to ask for a new one. The token is only ever sent to the
 * API: it is not shown, stored or logged, and the page is served with `Referrer-Policy: no-referrer` (next.config.ts).
 */
export function ResetPasswordForm() {
  const params = useSearchParams();
  const email = params.get("email") ?? "";
  const token = params.get("token") ?? "";
  const t = useTranslations("auth.reset");
  const validation = useTranslations("validation");
  const apiErrors = useApiErrors();
  const fieldMessage = useApiFieldMessage();
  const schema = useMemo(() => createResetPasswordSchema(validation), [validation]);
  const form = useForm<ResetPasswordValues, unknown, ResetPasswordData>({
    resolver: zodResolver(schema),
    defaultValues: EMPTY_FORM,
  });
  const { errors } = form.formState;
  const retry = useRetryAfter();

  const reset = useMutation({
    mutationFn: (values: ResetPasswordData) => confirmPasswordReset({ email, token, newPassword: values.newPassword }),
    onSuccess: () => {
      // Pages stay mounted across navigations (cacheComponents), so clear the form, passwords included.
      form.reset(EMPTY_FORM);
    },
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
      if (error.code === "validation_failed" && error.fieldErrors.newPassword?.length) {
        form.setError("newPassword", { message: fieldMessage("newPassword") });
        return;
      }
      if (FORM_LEVEL_CODES.has(error.code)) {
        form.setError("root", { message: apiErrors.message(error.code) });
        return;
      }
      if (error.code === "invalid_or_expired_reset_link") {
        form.reset(EMPTY_FORM);
        return;
      }
      toast.error(apiErrors.message(error.code));
    },
  });

  const badLink = reset.error instanceof ApiError && reset.error.code === "invalid_or_expired_reset_link";
  if (email === "" || token === "" || badLink) {
    return <InvalidLink />;
  }

  if (reset.isSuccess) {
    return (
      <div className="flex flex-col gap-4">
        <p role="status" className="rounded-md border border-emerald-600/40 bg-emerald-50 px-3 py-2 text-sm dark:bg-emerald-950/30">
          {t("done")}
        </p>
        <p className="text-sm">
          <Link href="/login" className="underline underline-offset-4">
            {t("signIn")}
          </Link>
        </p>
      </div>
    );
  }

  return (
    <form noValidate aria-label={t("form")} className="flex flex-col gap-5" onSubmit={form.handleSubmit((values) => {
        retry.clear();
        reset.mutate(values);
      })}>
      {errors.root?.message ? (
        <p role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive">
          {errors.root.message}
        </p>
      ) : null}

      <div className="flex flex-col gap-2">
        <Label htmlFor="reset-new-password">{t("newPassword")}</Label>
        <Input
          id="reset-new-password"
          type="password"
          autoComplete="new-password"
          aria-invalid={errors.newPassword ? true : undefined}
          aria-describedby={errors.newPassword ? "reset-new-password-error" : "reset-new-password-hint"}
          {...form.register("newPassword")}
        />
        <p id="reset-new-password-hint" className="text-sm text-muted-foreground">
          {t("newPasswordHint", { min: PASSWORD_MIN_LENGTH })}
        </p>
        {errors.newPassword?.message ? (
          <p id="reset-new-password-error" className="text-sm text-destructive">
            {errors.newPassword.message}
          </p>
        ) : null}
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor="reset-confirmation">{t("confirmation")}</Label>
        <Input
          id="reset-confirmation"
          type="password"
          autoComplete="new-password"
          aria-invalid={errors.confirmation ? true : undefined}
          aria-describedby={errors.confirmation ? "reset-confirmation-error" : undefined}
          {...form.register("confirmation")}
        />
        {errors.confirmation?.message ? (
          <p id="reset-confirmation-error" className="text-sm text-destructive">
            {errors.confirmation.message}
          </p>
        ) : null}
      </div>

      <RateLimitMessage retry={retry} />

      <Button type="submit" disabled={reset.isPending || retry.waiting}>
        {reset.isPending ? t("submitting") : t("submit")}
      </Button>
    </form>
  );
}
