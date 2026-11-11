"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { useEffect } from "react";
import { Button } from "@/components/ui/button";
import { PAGE_WIDTH } from "@/components/page-width";
import { useFocusOn } from "@/lib/focus";

type RouteErrorProps = {
  error: unknown;
  retry: () => void;
};

/**
 * The error page of a route: what happened in one sentence, a way to try again and a way out. It replaces the page, so it
 * takes focus (UX-002) instead of being a live region that would be read twice.
 */
export function RouteError({ error, retry }: RouteErrorProps) {
  const t = useTranslations("routeError");
  const focus = useFocusOn<HTMLHeadingElement>();

  useEffect(() => {
    console.error(error);
  }, [error]);

  return (
    <div className={`mx-auto flex w-full ${PAGE_WIDTH.narrow} flex-col items-start gap-4 px-4 py-12`}>
      <h1 {...focus} className="text-xl font-semibold">
        {t("title")}
      </h1>
      <p className="text-muted-foreground">{t("body")}</p>
      <div className="flex flex-wrap items-center gap-3">
        <Button onClick={() => retry()}>{t("retry")}</Button>
        <Button asChild variant="outline">
          <Link href="/">{t("home")}</Link>
        </Button>
      </div>
    </div>
  );
}
