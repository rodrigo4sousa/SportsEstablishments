import type { Metadata } from "next";
import { Suspense } from "react";
import { GuestOnly } from "@/components/guest-only";
import { ForgotPasswordForm } from "@/features/auth/components/forgot-password-form";

export const metadata: Metadata = {
  title: "Esqueci-me da palavra-passe · Reservas de Campos",
};

export default function Page() {
  return (
    <main className="mx-auto flex w-full max-w-md flex-1 flex-col gap-6 px-4 py-12">
      <div className="flex flex-col gap-2">
        <h1 className="text-2xl font-semibold tracking-tight">Esqueci-me da palavra-passe</h1>
        <p className="text-muted-foreground">Indica o email da tua conta e enviamos-te uma ligação para escolheres uma nova palavra-passe.</p>
      </div>
      {/* A signed-in user is sent to the home of their profile. */}
      <Suspense fallback={null}>
        <GuestOnly>
          <ForgotPasswordForm />
        </GuestOnly>
      </Suspense>
    </main>
  );
}
