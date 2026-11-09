import "server-only";
import { isIP } from "node:net";
import { headers } from "next/headers";

/**
 * The header that tells the API who the visitor is, for server-side calls (T06): without it every page rendered on the server would count
 * as one client in the API's per-IP rate limit. Taken from the page's own request. On Vercel that header holds the visitor's public address
 * (Vercel overwrites what the browser sends). Only the last entry is used, the one added by the nearest proxy; anything that is not an IP is
 * dropped, so a client cannot put arbitrary text in a header sent to the API.
 */
export async function visitorIpHeader(): Promise<Record<string, string>> {
  const forwardedFor = (await headers()).get("x-forwarded-for") ?? "";
  const address = forwardedFor.split(",").at(-1)?.trim() ?? "";
  return isIP(address) === 0 ? {} : { "X-Forwarded-For": address };
}
