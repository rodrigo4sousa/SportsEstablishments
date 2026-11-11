import type { Metadata } from "next";
import { getTranslations } from "next-intl/server";
import Link from "next/link";
import { Suspense } from "react";
import { useTranslations } from "next-intl";
import { Main } from "@/components/main";
import { PublicHeader } from "@/components/public-header";
import { GuestOnly } from "@/components/guest-only";
import { RegisterForm } from "@/features/auth/components/register-form";

export async function generateMetadata(): Promise<Metadata> {
  const t = await getTranslations("pages.registerOwner");
  return {
    title: t("title"),
  };
}

export default function Page() {
  const t = useTranslations("pages.registerOwner");
  return (
    <>
      <PublicHeader account={false} />
      <Main className="mx-auto flex w-full max-w-md flex-1 flex-col gap-6 px-4 py-12">
        <div className="flex flex-col gap-2">
          <h1 className="text-2xl font-semibold tracking-tight">{t("title")}</h1>
          <p className="text-muted-foreground">{t("intro")}</p>
        </div>
        <Suspense fallback={null}>
          <GuestOnly>
            <RegisterForm variant="owner" />
            <p className="text-sm text-muted-foreground">
              {t.rich("playerPrompt", {
                link: (chunks) => (
                  <Link href="/register" className="underline underline-offset-4">
                    {chunks}
                  </Link>
                ),
              })}
            </p>
          </GuestOnly>
        </Suspense>
        <p className="text-sm text-muted-foreground">
          {t.rich("haveAccount", {
            link: (chunks) => (
              <Link href="/login" className="underline underline-offset-4">
                {chunks}
              </Link>
            ),
          })}
        </p>
      </Main>
    </>
  );
}
