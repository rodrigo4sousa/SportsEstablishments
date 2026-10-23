import type { ReactNode } from "react";

type PageShellProps = {
  title: string;
  children: ReactNode;
};

/** The body of a page inside an area: a title and the content, in the same column as the area header. */
export function PageShell({ title, children }: PageShellProps) {
  return (
    <section className="mx-auto flex w-full max-w-3xl flex-1 flex-col gap-6 px-4 py-10">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      {children}
    </section>
  );
}
