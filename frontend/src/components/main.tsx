import type { ReactNode } from "react";

/** The target of the «Saltar para o conteúdo» link (UX-014): every page has exactly one `<main id={MAIN_ID}>`. */
export const MAIN_ID = "main-content";

type MainProps = {
  className?: string;
  children: ReactNode;
};

/** The page's `<main>`. It takes focus when the skip link is used, and has no ring of its own: it is not a control. */
export function Main({ className = "flex flex-1 flex-col", children }: MainProps) {
  return (
    <main id={MAIN_ID} tabIndex={-1} className={`${className} focus-visible:outline-none`}>
      {children}
    </main>
  );
}
