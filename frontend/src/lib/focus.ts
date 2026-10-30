"use client";

import { useEffect, useRef } from "react";

/**
 * UX-002, the rule: when an action replaces the element that has focus (a button that becomes a confirmation, a form that
 * becomes its result), focus goes to what replaced it. Without it the browser puts focus back on `<body>`, the next Tab
 * starts at the top of the page, and a keyboard or screen-reader user has to find their place again.
 *
 * `useFocusOn` is for the element that appears: spread the returned props on it. It is focused on the first render, and again
 * whenever `trigger` changes to something truthy (a new result replacing the old one). Focusing also scrolls the element into
 * view, so a result that would be far off screen is brought to the user.
 *
 * Do not make the focused element a live region (`role="status"`, `role="alert"`, `aria-live`): the screen reader reads it
 * because it has focus, and a live region would read it a second time. A message that appears while focus stays where it was
 * (a field error under the field being typed in) is the other case, and keeps its live region.
 */
export function useFocusOn<T extends HTMLElement>(trigger: unknown = true) {
  const ref = useRef<T>(null);

  useEffect(() => {
    if (trigger) {
      ref.current?.focus();
    }
  }, [trigger]);

  return { ref, tabIndex: -1 } as const;
}

/**
 * The other half of the rule, for the control that opened something: when it closes and focus was lost to `<body>` (the
 * thing that had focus was removed, e.g. «Manter reserva»), focus returns to the control that opened it. If something else
 * already took focus (a result message), it is left alone. Attach `ref` to the opening control.
 */
export function useRestoreFocus<T extends HTMLElement>(open: boolean) {
  const ref = useRef<T>(null);
  const wasOpen = useRef(open);

  useEffect(() => {
    if (wasOpen.current && !open) {
      const active = document.activeElement;
      if (!active || active === document.body) {
        ref.current?.focus();
      }
    }
    wasOpen.current = open;
  }, [open]);

  return ref;
}
