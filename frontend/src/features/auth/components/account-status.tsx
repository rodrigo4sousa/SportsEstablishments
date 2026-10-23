"use client";

import Link from "next/link";
import { Button } from "@/components/ui/button";
import { useCurrentUser } from "../hooks/use-current-user";

/** Header corner: who is signed in, or the way to create an account. */
export function AccountStatus() {
  const { data: account, isPending } = useCurrentUser();

  if (isPending) {
    return null;
  }

  if (account) {
    return (
      <p className="text-sm">
        Olá, <span className="font-medium">{account.name}</span>
      </p>
    );
  }

  return (
    <Button asChild variant="outline" size="sm">
      <Link href="/register">Criar conta</Link>
    </Button>
  );
}
