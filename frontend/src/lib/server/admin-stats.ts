import "server-only";
import { cookies } from "next/headers";
import { toAdminStats, type AdminStats } from "@/features/admin/types";
import type { components } from "@/lib/api/schema";
import { apiInternalUrl } from "./api-url";
import { visitorIpHeader } from "./visitor-ip";

/**
 * The platform's totals for the first load of `/admin` (US24), read on the server with the admin's own cookies so the
 * numbers are already in the HTML. Anything but a good answer is null: the page then loads the numbers in the browser,
 * which can renew an expired session and shows a proper error.
 */
export async function loadAdminStats(): Promise<AdminStats | null> {
  const cookie = (await cookies()).toString();
  try {
    const response = await fetch(`${apiInternalUrl()}/api/admin/stats`, {
      headers: { Cookie: cookie, Accept: "application/json", ...(await visitorIpHeader()) },
      cache: "no-store",
    });
    return response.ok ? toAdminStats((await response.json()) as components["schemas"]["AdminStatsResponse"]) : null;
  } catch {
    return null;
  }
}
