import type { Metadata } from "next";
import Link from "next/link";
import { RegisterForm } from "@/features/auth/components/register-form";

export const metadata: Metadata = {
  title: "Criar conta · Reservas de Campos",
};

export default function Page() {
  return (
    <main className="mx-auto flex w-full max-w-md flex-1 flex-col gap-6 px-4 py-12">
      <div className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold tracking-tight">Criar conta</h1>
        <p className="text-muted-foreground">Cria a tua conta de jogador para reservar campos e gerir as tuas reservas.</p>
      </div>
      <RegisterForm />
      <p className="text-sm text-muted-foreground">
        <Link href="/" className="underline underline-offset-4">
          Voltar ao início
        </Link>
      </p>
    </main>
  );
}
