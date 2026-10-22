type AreaPlaceholderProps = {
  title: string;
  description: string;
};

/** Temporary page body for areas whose stories are not implemented yet. */
export function AreaPlaceholder({ title, description }: AreaPlaceholderProps) {
  return (
    <section className="mx-auto flex w-full max-w-3xl flex-1 flex-col gap-2 px-4 py-12">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      <p className="text-muted-foreground">{description}</p>
    </section>
  );
}
