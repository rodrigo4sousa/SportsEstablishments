"use client";

import Link from "next/link";
import { Button } from "@/components/ui/button";
import { LogoutButton } from "./logout-button";
import { useCurrentUser } from "../hooks/use-current-user";

/** Header corner: who is signed in, or the way to create an account. */
export function AccountStatus() {
  const { data: account, isPending } = useCurrentUser();

  if (isPending) {
    return null;
  }

  if (account) {
    return (
      <nav aria-label="Conta" className="flex items-center gap-3">
        {account.profile === "Player" ? (
          <Link href="/bookings" className="text-sm underline-offset-4 hover:underline">
            As minhas reservas
          </Link>
        ) : null}
        <p className="text-sm">
          Olá, <span className="font-medium">{account.name}</span>
        </p>
        <LogoutButton />
      </nav>
    );
  }

  return (
    <nav aria-label="Conta" className="flex items-center gap-2">
      <Button asChild variant="ghost" size="sm">
        <Link href="/login">Entrar</Link>
      </Button>
      <Button asChild variant="outline" size="sm">
        <Link href="/register">Criar conta</Link>
      </Button>
    </nav>
  );
}
