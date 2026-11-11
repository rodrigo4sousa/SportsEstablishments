"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { RateLimitMessage } from "@/components/rate-limit-message";
import { ResultNotice } from "@/components/result-notice";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { currentUserQueryKey } from "@/features/auth/api";
import { useCurrentUser } from "@/features/auth/hooks/use-current-user";
import { ContactEmailLink } from "@/features/legal/components/legal-document";
import { useApiErrors } from "@/lib/api/error-messages";
import { isExpectedApiError } from "@/lib/api/expected-error";
import { useApiFieldMessage } from "@/lib/api/form-errors";
import { useRetryAfter } from "@/lib/api/use-retry-after";
import { loginUrl } from "@/lib/auth/redirects";
import { useFocusOn, useRestoreFocus } from "@/lib/focus";
import { deleteMyAccount, downloadMyData, EXPORT_FILE_NAME } from "../api";
import { EmailLanguage } from "./email-language";
import { createDeleteAccountSchema, type DeleteAccountData, type DeleteAccountValues } from "../schemas/delete-account";

const ACCOUNT_PATH = "/account";

/**
 * US30: «Privacidade e dados» on the account page: a copy of the person's data, and deleting the account. T09: above it, the language of
 * the person's emails (not for the admin, who gets none).
 */
export function PrivacySection() {
  const t = useTranslations("account");
  const [deleted, setDeleted] = useState(false);
  const { data: account } = useCurrentUser();

  if (deleted) {
    return <AccountDeleted />;
  }

  return (
    <>
    {account && account.profile !== "Admin" ? <EmailLanguage key={account.id} account={account} /> : null}
    <section aria-labelledby="privacy-heading" className="flex flex-col gap-6">
      <h2 id="privacy-heading" className="text-lg font-semibold">
        {t("privacy")}
      </h2>
      <ExportData />
      {account?.profile === "Admin" ? (
        <p className="text-sm text-muted-foreground">{t("adminCannotDelete")}</p>
      ) : (
        <DeleteAccount onDeleted={() => setDeleted(true)} />
      )}
    </section>
    </>
  );
}

function ExportData() {
  const t = useTranslations("account.export");
  const apiErrors = useApiErrors();
  const router = useRouter();
  const retry = useRetryAfter();
  const [refusal, setRefusal] = useState<string | null>(null);
  const download = useMutation({
    mutationFn: downloadMyData,
    onMutate: () => setRefusal(null),
    onError: (error) => {
      if (!isExpectedApiError(error)) {
        toast.error(apiErrors.unexpected(error));
        return;
      }
      if (error.status === 401) {
        router.replace(loginUrl(ACCOUNT_PATH));
        return;
      }
      if (error.code === "too_many_requests" && error.retryAfterSeconds !== undefined) {
        retry.start(error.retryAfterSeconds);
        return;
      }
      setRefusal(apiErrors.message(error.code));
    },
  });

  return (
    <div className="flex flex-col items-start gap-3">
      <p className="text-sm text-muted-foreground">{t("intro")}</p>
      <Button type="button" variant="outline" size="cta" disabled={download.isPending || retry.waiting} onClick={() => download.mutate()}>
        {download.isPending ? t("preparing") : t("button")}
      </Button>
      <RateLimitMessage retry={retry} />
      {refusal ? <ResultNotice kind="error">{refusal}</ResultNotice> : null}
      {download.isSuccess ? <ResultNotice kind="success">{t("done", { file: EXPORT_FILE_NAME })}</ResultNotice> : null}
    </div>
  );
}

function DeleteAccount({ onDeleted }: { onDeleted: () => void }) {
  const t = useTranslations("account.delete");
  const validation = useTranslations("validation");
  const apiErrors = useApiErrors();
  const fieldMessage = useApiFieldMessage("account");
  const schema = useMemo(() => createDeleteAccountSchema(validation), [validation]);
  const router = useRouter();
  const queryClient = useQueryClient();
  const retry = useRetryAfter();
  const [confirming, setConfirming] = useState(false);
  const [refusal, setRefusal] = useState<{ code: string } | null>(null);
  const askButton = useRestoreFocus<HTMLButtonElement>(confirming);
  const form = useForm<DeleteAccountValues, unknown, DeleteAccountData>({
    resolver: zodResolver(schema),
    defaultValues: { password: "" },
  });
  const { errors } = form.formState;

  const close = () => {
    // The password must not stay in a form that is kept mounted.
    form.reset({ password: "" });
    setConfirming(false);
  };

  const remove = useMutation({
    mutationFn: (values: DeleteAccountData) => deleteMyAccount(values.password),
    onSuccess: () => {
      form.reset({ password: "" });
      onDeleted();
      // The API cleared the cookies: the header becomes a visitor's at once, and nothing fetched for this person stays in memory.
      queryClient.removeQueries({ predicate: (query) => query.queryKey[0] !== currentUserQueryKey[0] });
      queryClient.setQueryData(currentUserQueryKey, null);
    },
    onError: (error) => {
      form.resetField("password", { defaultValue: "" });
      if (!isExpectedApiError(error)) {
        toast.error(apiErrors.unexpected(error));
        return;
      }
      if (error.status === 401) {
        router.replace(loginUrl(ACCOUNT_PATH));
        return;
      }
      if (error.code === "too_many_requests" && error.retryAfterSeconds !== undefined) {
        retry.start(error.retryAfterSeconds);
        return;
      }
      if (error.fieldErrors.password?.length) {
        // A wrong password has its own code; any other refusal of the field is a missing one.
        const message = error.code === "invalid_password" ? apiErrors.message(error.code) : fieldMessage("password");
        form.setError("password", { message }, { shouldFocus: true });
        return;
      }
      close();
      setRefusal({ code: error.code });
    },
  });

  return (
    <div className="flex flex-col items-start gap-3 border-t pt-6">
      <p className="text-sm text-muted-foreground">{t("intro")}</p>
      {refusal ? (
        <ResultNotice kind="error">
          {apiErrors.message(refusal.code)}
          {refusal.code === "owner_has_clubs" ? (
            <>
              {" "}
              <ContactEmailLink />
            </>
          ) : null}
        </ResultNotice>
      ) : null}
      {confirming ? (
        <ConfirmDeletion>
          <form
            noValidate
            aria-label={t("form")}
            className="flex flex-col gap-4"
            onSubmit={form.handleSubmit((values) => remove.mutate(values))}
          >
            <div className="flex flex-col gap-2">
              <Label htmlFor="delete-account-password">{t("password")}</Label>
              <Input
                id="delete-account-password"
                type="password"
                autoComplete="current-password"
                aria-invalid={errors.password ? true : undefined}
                aria-describedby={errors.password ? "delete-account-password-error" : undefined}
                {...form.register("password")}
              />
              {errors.password ? (
                <p id="delete-account-password-error" role="alert" className="text-sm text-destructive">
                  {errors.password.message}
                </p>
              ) : null}
            </div>
            <RateLimitMessage retry={retry} />
            <div className="flex flex-wrap items-center gap-3">
              <Button type="submit" variant="destructive" size="cta" disabled={remove.isPending || retry.waiting}>
                {remove.isPending ? t("deleting") : t("confirm")}
              </Button>
              <Button type="button" variant="outline" size="cta" disabled={remove.isPending} onClick={close}>
                {t("keep")}
              </Button>
            </div>
          </form>
        </ConfirmDeletion>
      ) : (
        <Button
          ref={askButton}
          type="button"
          variant="outline"
          size="cta"
          onClick={() => {
            setRefusal(null);
            setConfirming(true);
          }}
        >
          {t("button")}
        </Button>
      )}
    </div>
  );
}

/** What deleting does, said before the password is asked for. Asking replaces the button, so focus goes to it (UX-002). */
function ConfirmDeletion({ children }: { children: React.ReactNode }) {
  const focus = useFocusOn<HTMLDivElement>();
  const t = useTranslations("account.delete");
  return (
    <div role="group" aria-label={t("confirmGroup")} {...focus} className="flex w-full flex-col gap-4 rounded-md border border-destructive/40 bg-destructive/5 p-4">
      <p className="text-sm font-medium">{t("question")}</p>
      <ul className="list-disc space-y-1 pl-5 text-sm">
        <li>{t("effectBookings")}</li>
        <li>{t("effectData")}</li>
        <li>{t("effectSessions")}</li>
        <li>{t("effectNoEmail")}</li>
      </ul>
      {children}
    </div>
  );
}

/** Replaces the section: the form that had focus is gone, so the heading takes it (UX-002). */
function AccountDeleted() {
  const focus = useFocusOn<HTMLHeadingElement>();
  const t = useTranslations("account.delete");
  return (
    <section aria-labelledby="account-deleted" className="flex flex-col items-start gap-3">
      <h2 id="account-deleted" {...focus} className="text-lg font-semibold">
        {t("deletedTitle")}
      </h2>
      <p className="text-sm">{t("deletedBody")}</p>
      <Link href="/" className="text-sm underline underline-offset-4">
        {t("home")}
      </Link>
    </section>
  );
}
