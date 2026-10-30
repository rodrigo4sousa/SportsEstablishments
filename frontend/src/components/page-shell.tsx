import type { ReactNode } from "react";
import { PAGE_WIDTH, type PageWidth } from "@/components/page-width";

type PageShellProps = {
  title: string;
  /** Above the title: a way back. */
  before?: ReactNode;
  /** Under the title: one or two sentences saying what the page is. */
  description?: ReactNode;
  width?: PageWidth;
  children: ReactNode;
};

/** The body of a page inside an area: a title and the content, in the same column as the area header. */
export function PageShell({ title, before, description, width = "narrow", children }: PageShellProps) {
  return (
    <section className={`mx-auto flex w-full ${PAGE_WIDTH[width]} flex-1 flex-col gap-6 px-4 py-10`}>
      {before}
      {description ? (
        <div className="flex flex-col gap-2">
          <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
          {description}
        </div>
      ) : (
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      )}
      {children}
    </section>
  );
}
