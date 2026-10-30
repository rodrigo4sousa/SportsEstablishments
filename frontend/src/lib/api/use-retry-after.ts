"use client";

import { useEffect, useState } from "react";

export type RetryAfter = {
  /** Whole seconds left to wait; 0 when not waiting. */
  remaining: number;
  /** A wait is running: the form's button is disabled. */
  waiting: boolean;
  /** A wait ran and is over. */
  expired: boolean;
  /** Changes with every wait, so a message can take focus once per wait. */
  startedAt: number | null;
  start: (seconds: number) => void;
  clear: () => void;
};

/**
 * UX-018: a 429 says how long to wait (`Retry-After`); the form says it, counts it down and keeps its button disabled until then,
 * instead of letting each click repeat the 429. The countdown ticks once a second, and only while it runs.
 */
export function useRetryAfter(): RetryAfter {
  const [until, setUntil] = useState<number | null>(null);
  const [startedAt, setStartedAt] = useState<number | null>(null);
  const [now, setNow] = useState(0);
  const waiting = until !== null && until > now;

  useEffect(() => {
    if (!waiting) return;
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, [waiting]);

  return {
    remaining: waiting ? Math.ceil((until - now) / 1000) : 0,
    waiting,
    expired: until !== null && !waiting,
    startedAt,
    start: (seconds) => {
      const t = Date.now();
      setNow(t);
      setUntil(t + seconds * 1000);
      setStartedAt(t);
    },
    clear: () => {
      setUntil(null);
      setStartedAt(null);
    },
  };
}
