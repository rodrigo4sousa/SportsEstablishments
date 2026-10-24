import { errorMessage } from "@/lib/api/error-messages";

/**
 * The message for an API error on the public pages. The shared text for `club_not_found` talks to an owner ("não é
 * teu"); a visitor is told that the club is no longer available, which also does not say why (it may have been hidden).
 */
export function publicErrorMessage(code: string): string {
  return code === "club_not_found" ? "Este clube já não está disponível." : errorMessage(code);
}
