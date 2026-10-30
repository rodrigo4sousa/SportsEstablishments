import { cn } from "cn"

/** A grey block that pulses where content is about to appear. Decorative: pair it with a text for screen readers (`LoadingRegion`). */
function Skeleton({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      aria-hidden="true"
      data-slot="skeleton"
      className={cn("animate-pulse rounded-md bg-foreground/10 motion-reduce:animate-none", className)}
      {...props}
    />
  )
}

export { Skeleton }
