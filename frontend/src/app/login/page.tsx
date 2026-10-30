import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { Main } from "@/components/main";
import { PublicHeader } from "@/components/public-header";
import { GuestOnly } from "@/components/guest-only";
import { LoginScreen } from "@/features/auth/components/login-screen";

export const metadata: Metadata = {
  title: "Entrar",
};

export default function Page() {
  return (
    <>
      <PublicHeader account={false} />
      <Main className="mx-auto flex w-full max-w-md flex-1 flex-col gap-6 px-4 py-12">
        <div className="flex flex-col gap-2">
          <h1 className="text-2xl font-semibold tracking-tight">Entrar</h1>
          <p className="text-muted-foreground">Entra na tua conta para reservar campos ou gerir o teu clube.</p>
        </div>
        {/* A signed-in user is sent to their area; the screen itself reads the `next` search parameter in the browser. */}
        <Suspense fallback={null}>
          <GuestOnly>
            <LoginScreen />
          </GuestOnly>
        </Suspense>
        <p className="text-sm text-muted-foreground">
          <Link href="/" className="underline underline-offset-4">
            Voltar ao início
          </Link>
        </p>
      </Main>
    </>
  );
}
