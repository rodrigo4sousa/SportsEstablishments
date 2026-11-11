"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";
import { ResultNotice } from "@/components/result-notice";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { NativeSelect } from "@/components/ui/native-select";
import { currentUserQueryKey, type Account } from "@/features/auth/api";
import { DEFAULT_LOCALE, isAppLocale, type AppLocale } from "@/i18n/locales";
import { useApiErrors } from "@/lib/api/error-messages";
import { isExpectedApiError } from "@/lib/api/expected-error";
import { loginUrl } from "@/lib/auth/redirects";
import { changePreferredLanguage } from "../api";

const ACCOUNT_PATH = "/account";

/**
 * T09: the language of the person's emails (`users.preferred_language`). No preference (every account made before T09) reads as
 * Portuguese, which is what the emails use for it. It is separate from the site's language (the PT | EN switch, a cookie): changing one
 * never changes the other.
 */
export function EmailLanguage({ account }: { account: Account }) {
  const t = useTranslations("account.language");
  const apiErrors = useApiErrors();
  const router = useRouter();
  const queryClient = useQueryClient();
  const stored = isAppLocale(account.preferredLanguage) ? account.preferredLanguage : DEFAULT_LOCALE;
  const [choice, setChoice] = useState<AppLocale>(stored);
  const [refusal, setRefusal] = useState<string | null>(null);

  const save = useMutation({
    mutationFn: changePreferredLanguage,
    onMutate: () => setRefusal(null),
    onSuccess: (_, language) => {
      queryClient.setQueryData<Account | null>(currentUserQueryKey, (current) => (current ? { ...current, preferredLanguage: language } : current));
    },
    onError: (error) => {
      if (!isExpectedApiError(error)) {
        toast.error(apiErrors.unexpected(error));
        return;
      }
      if (error.status === 401) {
        router.replace(loginUrl(ACCOUNT_PATH));
        return;
      }
      setRefusal(apiErrors.message(error.code));
    },
  });

  return (
    <section aria-labelledby="email-language-heading" className="flex flex-col items-start gap-3">
      <h2 id="email-language-heading" className="text-lg font-semibold">
        {t("heading")}
      </h2>
      <p className="text-sm text-muted-foreground">{t("intro")}</p>
      <form
        noValidate
        className="flex flex-wrap items-end gap-3"
        onSubmit={(event) => {
          event.preventDefault();
          save.mutate(choice);
        }}
      >
        <div className="flex flex-col gap-2">
          <Label htmlFor="email-language">{t("label")}</Label>
          <NativeSelect
            id="email-language"
            value={choice}
            onChange={(event) => {
              save.reset();
              if (isAppLocale(event.target.value)) setChoice(event.target.value);
            }}
          >
            <option value="pt-PT">{t("pt")}</option>
            <option value="en">{t("en")}</option>
          </NativeSelect>
        </div>
        <Button type="submit" variant="outline" size="cta" disabled={save.isPending}>
          {save.isPending ? t("saving") : t("save")}
        </Button>
      </form>
      {refusal ? <ResultNotice kind="error">{refusal}</ResultNotice> : null}
      {save.isSuccess ? <ResultNotice kind="success">{t("saved")}</ResultNotice> : null}
    </section>
  );
}
