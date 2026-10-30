"use client";

import type { ReactNode } from "react";
import { useFocusOn } from "@/lib/focus";

type ResultNoticeProps = {
  kind: "success" | "error";
  children: ReactNode;
  className?: string;
};

/**
 * The result of an action the user just took (booking cancelled, no-show marked, a refusal), shown where the action was. It
 * takes focus when it appears and again when its text changes, which also scrolls it into view (UX-002, UX-003, UX-004), and so
 * it is **not** a live region: the screen reader reads it because it has focus (see `useFocusOn`).
 */
export function ResultNotice({ kind, children, className = "" }: ResultNoticeProps) {
  const focus = useFocusOn<HTMLParagraphElement>(children);

  return (
    <p
      {...focus}
      data-kind={kind}
      className={`rounded-md border px-3 py-2 text-sm ${
        kind === "error"
          ? "border-destructive/40 bg-destructive/10 text-destructive"
          : "border-emerald-600/40 bg-emerald-50 dark:bg-emerald-950/30"
      } ${className}`}
    >
      {children}
    </p>
  );
}
