import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import { Suspense } from "react";
import { useTranslations } from "next-intl";
import { Main } from "@/components/main";
import { PublicHeader } from "@/components/public-header";
import { GuestOnly } from "@/components/guest-only";
import { ForgotPasswordForm } from "@/features/auth/components/forgot-password-form";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages.forgotPassword");
  return {
    title: t("title"),
  };
}

export default function Page() {
  const t = useTranslations("pages.forgotPassword");
  return (
    <>
      <PublicHeader account={false} />
      <Main className="mx-auto flex w-full max-w-md flex-1 flex-col gap-6 px-4 py-12">
        <div className="flex flex-col gap-2">
          <h1 className="text-2xl font-semibold tracking-tight">{t("title")}</h1>
          <p className="text-muted-foreground">{t("intro")}</p>
        </div>
        {/* A signed-in user is sent to the home of their profile. */}
        <Suspense fallback={null}>
          <GuestOnly>
            <ForgotPasswordForm />
          </GuestOnly>
        </Suspense>
      </Main>
    </>
  );
}
