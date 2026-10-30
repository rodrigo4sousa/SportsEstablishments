import type { ReactNode } from "react";

type EmptyStateProps = {
  /** What is missing, in one sentence. */
  title: string;
  /** Why, or what to do next. */
  description?: ReactNode;
  /** The next step (a link or a button), if there is one. */
  action?: ReactNode;
};

/** A list or table with nothing to show: say so, and offer the next step instead of leaving a blank area. */
export function EmptyState({ title, description, action }: EmptyStateProps) {
  return (
    <div role="status" className="flex flex-col items-start gap-2 rounded-md border border-dashed px-4 py-6">
      <p className="font-medium">{title}</p>
      {description ? <p className="text-sm text-muted-foreground">{description}</p> : null}
      {action ? <div className="pt-1">{action}</div> : null}
    </div>
  );
}
