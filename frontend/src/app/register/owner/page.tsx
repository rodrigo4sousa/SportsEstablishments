import type { Metadata } from "next";
import Link from "next/link";
import { Suspense } from "react";
import { Main } from "@/components/main";
import { PublicHeader } from "@/components/public-header";
import { GuestOnly } from "@/components/guest-only";
import { RegisterForm } from "@/features/auth/components/register-form";

export const metadata: Metadata = {
  title: "Criar conta de dono",
};

export default function Page() {
  return (
    <>
      <PublicHeader account={false} />
      <Main className="mx-auto flex w-full max-w-md flex-1 flex-col gap-6 px-4 py-12">
        <div className="flex flex-col gap-2">
          <h1 className="text-2xl font-semibold tracking-tight">Criar conta de dono</h1>
          <p className="text-muted-foreground">Cria a tua conta de dono para registar o teu clube e gerir os campos e as reservas.</p>
        </div>
        <Suspense fallback={null}>
          <GuestOnly>
            <RegisterForm variant="owner" />
            <p className="text-sm text-muted-foreground">
              Queres só reservar campos?{" "}
              <Link href="/register" className="underline underline-offset-4">
                Cria uma conta de jogador
              </Link>
            </p>
          </GuestOnly>
        </Suspense>
        <p className="text-sm text-muted-foreground">
          Já tens conta?{" "}
          <Link href="/login" className="underline underline-offset-4">
            Entrar
          </Link>
        </p>
      </Main>
    </>
  );
}
