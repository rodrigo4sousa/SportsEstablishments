"use client";

import { useFocusOn } from "@/lib/focus";
import type { RetryAfter } from "@/lib/api/use-retry-after";

/**
 * What a form says after a 429 that told it how long to wait (UX-018): how many seconds are left, ticking, and then that the
 * user can try again. The message takes focus when the wait starts (the button that was pressed is disabled now), so it is not a
 * live region; the countdown is a `timer`, which a screen reader does not announce at every tick, and the end of the wait has its
 * own polite status.
 */
export function RateLimitMessage({ retry }: { retry: RetryAfter }) {
  const focus = useFocusOn<HTMLParagraphElement>(retry.startedAt);
  const active = retry.waiting || retry.expired;

  return (
    <>
      {active ? (
        <p
          {...focus}
          data-kind={retry.waiting ? "error" : "success"}
          className={`rounded-md border px-3 py-2 text-sm ${
            retry.waiting ? "border-destructive/40 bg-destructive/10 text-destructive" : "border-emerald-600/40 bg-emerald-50 dark:bg-emerald-950/30"
          }`}
        >
          {retry.waiting ? (
            <>
              Fizeste demasiados pedidos seguidos. Podes tentar novamente dentro de{" "}
              <span role="timer" className="font-medium tabular-nums">
                {retry.remaining} {retry.remaining === 1 ? "segundo" : "segundos"}
              </span>
              .
            </>
          ) : (
            "Já podes tentar novamente."
          )}
        </p>
      ) : null}
      {/* Mounted when the wait starts, empty, so the words that fill it when the wait ends are announced. */}
      {retry.startedAt !== null ? (
        <span role="status" className="sr-only">
          {retry.expired ? "Já podes tentar novamente." : ""}
        </span>
      ) : null}
    </>
  );
}
