import type { ReactNode } from "react";

type LoadingRegionProps = {
  /** What is loading, in words: the one thing a screen reader hears (the skeleton blocks are hidden from it). */
  label: string;
  children: ReactNode;
  className?: string;
};

/** A loading state: skeleton blocks for the eye, and one polite status message for assistive technology. */
export function LoadingRegion({ label, children, className }: LoadingRegionProps) {
  return (
    <div role="status" className={className}>
      <span className="sr-only">{label}</span>
      {children}
    </div>
  );
}
