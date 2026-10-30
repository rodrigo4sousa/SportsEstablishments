import type { ReactNode } from "react";

type ScrollRegionProps = {
  /** What scrolls, in words: it names the region for screen readers and is read before its content. */
  label: string;
  children: ReactNode;
  className?: string;
};

/**
 * A box that scrolls sideways when its content is wider than the screen (a table, a calendar grid). Browsers do not put such a box
 * in the Tab order, so a keyboard user could not scroll it; `tabIndex={0}` does, and the `region` role with a name tells a screen
 * reader what it is (axe: scrollable-region-focusable). The focus ring is the global one.
 */
export function ScrollRegion({ label, children, className = "" }: ScrollRegionProps) {
  return (
    <div role="region" aria-label={label} tabIndex={0} className={`overflow-x-auto rounded-lg border ${className}`}>
      {children}
    </div>
  );
}
