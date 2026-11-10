import type { ReactNode } from "react";
import { redirect } from "next/navigation";
import { loginUrl } from "@/lib/auth/redirects";
import { getSessionAccount } from "@/lib/server/session";

/**
 * Server-side guard of the account page (US30), which every profile can open: without a session, to the login page and back. Reads the
 * request's cookies, so render it inside a `<Suspense>` boundary. The API enforces access on its own.
 */
export async function AccountGuard({ path, children }: { path: string; children: ReactNode }) {
  if ((await getSessionAccount()) === null) {
    redirect(loginUrl(path));
  }
  return <>{children}</>;
}
