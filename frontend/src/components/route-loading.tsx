import { LoadingRegion } from "@/components/loading-region";
import { PAGE_WIDTH } from "@/components/page-width";
import { Skeleton } from "@/components/ui/skeleton";

/** The loading page of a route: the shape of a page (a title and some blocks) instead of a line of text (UX-020). */
export function RouteLoading() {
  return (
    <LoadingRegion label="A carregar…" className={`mx-auto flex w-full ${PAGE_WIDTH.narrow} flex-col gap-4 px-4 py-10`}>
      <Skeleton className="h-8 w-48" />
      <Skeleton className="h-24 w-full" />
      <Skeleton className="h-24 w-full" />
    </LoadingRegion>
  );
}
