"use client";

import { useEffect } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { trySilentRefresh } from "@/lib/api/client";
import { safeNext } from "@/lib/auth/redirects";
import { currentUserQueryKey, fetchCurrentUser, type Account } from "../api";
import { LoginForm } from "./login-form";

/**
 * The login page body. The server cannot renew a session (the refresh cookie only travels to `/api/auth`), so a user
 * sent here with an expired access token is renewed silently first: if that works they continue to `next` (or the
 * home of their profile); otherwise the form shows.
 */
export function LoginScreen() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const next = useSearchParams().get("next");

  const silentSession = useQuery({
    queryKey: ["auth", "silent-session"],
    queryFn: async (): Promise<Account | null> => ((await trySilentRefresh()) ? fetchCurrentUser() : null),
    retry: false,
    gcTime: 0,
    staleTime: 0,
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  });

  const account = silentSession.data ?? null;
  useEffect(() => {
    if (account) {
      queryClient.setQueryData(currentUserQueryKey, account);
      router.replace(safeNext(next, account.profile));
    }
  }, [account, next, queryClient, router]);

  if (silentSession.isPending || account) {
    return (
      <p role="status" className="text-muted-foreground">
        A verificar a sessão…
      </p>
    );
  }

  return (
    <>
      <LoginForm next={next} />
      <p className="text-sm text-muted-foreground">
        Ainda não tens conta?{" "}
        <Link href="/register" className="underline underline-offset-4">
          Criar conta
        </Link>
      </p>
    </>
  );
}
