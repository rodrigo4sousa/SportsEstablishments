import { useMemo } from "react";
import { useTranslations } from "next-intl";
import { ApiError, NETWORK_ERROR } from "./client";

/** The translator of the `apiErrors` namespace, as `useTranslations("apiErrors")` gives it. */
type ApiErrorTranslator = ReturnType<typeof useTranslations<"apiErrors">>;

/** The code every message falls back to: an error the site has no words for (a newer API, a 5xx). */
export const UNKNOWN_ERROR = "unknown_error";

/**
 * The words for a stable API error `code` (docs/stack.md: the front end picks the message by code), in the page's language (T09). The
 * server's own text is never shown: a code the catalogs do not have gets the general message.
 */
export function apiErrorMessage(t: ApiErrorTranslator, code: string): string {
  return t.has(code as never) ? t(code as never) : t(UNKNOWN_ERROR);
}

/** The code of an unexpected failure: a request that got no answer says so (the user can check the connection), anything else is unknown. */
export function unexpectedErrorCode(error: unknown): string {
  return error instanceof ApiError && error.code === NETWORK_ERROR ? NETWORK_ERROR : UNKNOWN_ERROR;
}

/**
 * `message(code)`: the words for an API error code; `unexpected(error)`: the toast text for an unexpected failure. The details of an
 * unexpected failure stay in the console and the API log, never on screen.
 */
export function useApiErrors() {
  const t = useTranslations("apiErrors");
  return useMemo(
    () => ({
      message: (code: string) => apiErrorMessage(t, code),
      unexpected: (error: unknown) => apiErrorMessage(t, unexpectedErrorCode(error)),
    }),
    [t],
  );
}
