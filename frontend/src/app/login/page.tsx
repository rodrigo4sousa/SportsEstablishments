import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import Link from "next/link";
import { Suspense } from "react";
import { useTranslations } from "next-intl";
import { Main } from "@/components/main";
import { PublicHeader } from "@/components/public-header";
import { GuestOnly } from "@/components/guest-only";
import { LoginScreen } from "@/features/auth/components/login-screen";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages.login");
  return {
    title: t("title"),
  };
}

export default function Page() {
  const t = useTranslations("pages.login");
  return (
    <>
      <PublicHeader account={false} />
      <Main className="mx-auto flex w-full max-w-md flex-1 flex-col gap-6 px-4 py-12">
        <div className="flex flex-col gap-2">
          <h1 className="text-2xl font-semibold tracking-tight">{t("title")}</h1>
          <p className="text-muted-foreground">{t("intro")}</p>
        </div>
        {/* A signed-in user is sent to their area; the screen itself reads the `next` search parameter in the browser. */}
        <Suspense fallback={null}>
          <GuestOnly>
            <LoginScreen />
          </GuestOnly>
        </Suspense>
        <p className="text-sm text-muted-foreground">
          <Link href="/" className="underline underline-offset-4">
            {t("home")}
          </Link>
        </p>
      </Main>
    </>
  );
}
