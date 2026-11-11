import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { useTranslations } from "next-intl";
import { Main } from "@/components/main";
import { PublicHeader } from "@/components/public-header";
import { ResetPasswordForm } from "@/features/auth/components/reset-password-form";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages.resetPassword");
  return {
    title: t("title"),
    // The address holds a secret token: never let a search engine keep it.
    robots: { index: false, follow: false },
  };
}

/**
 * US05: the page of the emailed link. Not wrapped in `GuestOnly`: someone who is signed in on this browser and opens the link must
 * still be able to set the password. It is served with `Referrer-Policy: no-referrer` (next.config.ts) and loads nothing from
 * another origin, so the token in the address cannot reach a third party.
 */
export default function Page() {
  const t = useTranslations("pages.resetPassword");
  return (
    <>
      <PublicHeader account={false} />
      <Main className="mx-auto flex w-full max-w-md flex-1 flex-col gap-6 px-4 py-12">
        <div className="flex flex-col gap-2">
          <h1 className="text-2xl font-semibold tracking-tight">{t("title")}</h1>
          <p className="text-muted-foreground">{t("intro")}</p>
        </div>
        {/* The email and token are read from the address in the browser (`useSearchParams`), inside a boundary (Cache Components). */}
        <Suspense fallback={null}>
          <ResetPasswordForm />
        </Suspense>
      </Main>
    </>
  );
}
