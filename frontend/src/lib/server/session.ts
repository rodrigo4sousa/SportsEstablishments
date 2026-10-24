import "server-only";
import { cookies } from "next/headers";
import type { Account } from "@/features/auth/api";
import { apiInternalUrl } from "./api-url";

/**
 * The signed-in account, read on the server from `GET /api/auth/me` with the user's own cookies; null when there is
 * no valid access token. The server never refreshes a session: the refresh cookie only travels to `/api/auth`, so
 * page requests do not carry it. An expired session sends the user to `/login`, where the browser renews it.
 * Throws on any other failure (API down), so the area's error page shows instead of a wrong redirect.
 */
export async function getSessionAccount(): Promise<Account | null> {
  const cookie = (await cookies()).toString();
  if (cookie === "") {
    return null;
  }

  const response = await fetch(`${apiInternalUrl()}/api/auth/me`, {
    headers: { Cookie: cookie, Accept: "application/json" },
    cache: "no-store",
  });
  if (response.status === 401) {
    return null;
  }
  if (!response.ok) {
    throw new Error(`GET /api/auth/me failed with ${response.status}`);
  }
  return (await response.json()) as Account;
}
