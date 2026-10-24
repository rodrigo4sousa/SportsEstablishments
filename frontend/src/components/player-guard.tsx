import type { ReactNode } from "react";
import { headers } from "next/headers";
import { redirect } from "next/navigation";
import { areaDecision } from "@/lib/auth/redirects";
import { REQUEST_PATH_HEADER } from "@/lib/auth/request-path";
import { getSessionAccount } from "@/lib/server/session";

type PlayerGuardProps = {
  /** Where a visitor without a session returns to after signing in, when the proxy gave no path. */
  fallbackPath: string;
  children: ReactNode;
};

/**
 * Server-side guard of the pages only a player can use (US19, "As minhas reservas"): no session goes to
 * `/login?next=<this page>`, an owner or an admin goes to their own area. Unlike the owner and admin areas it adds no
 * header: the player layout already has one. Reads the request's cookies, so render it inside a `<Suspense>` boundary.
 * The API enforces access on its own.
 */
export async function PlayerGuard({ fallbackPath, children }: PlayerGuardProps) {
  const account = await getSessionAccount();
  const requestedPath = (await headers()).get(REQUEST_PATH_HEADER) ?? fallbackPath;
  const decision = areaDecision("Player", account, requestedPath);
  if (!decision.allowed) {
    redirect(decision.redirectTo);
  }
  return <>{children}</>;
}
