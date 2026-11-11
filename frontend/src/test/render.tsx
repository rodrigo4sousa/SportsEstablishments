import type { ReactElement } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import { Toaster } from "sonner";
import type { AppLocale } from "@/i18n/locales";
import { IntlProvider } from "@/test/intl";

/**
 * Renders with a fresh TanStack Query client (no retries), the toaster and the messages of one language (pt-PT by default), as the
 * app's root layout and Providers do.
 */
export function renderWithProviders(ui: ReactElement, { locale = "pt-PT" }: { locale?: AppLocale } = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  const result = render(
    <IntlProvider locale={locale}>
      <QueryClientProvider client={queryClient}>
        {ui}
        <Toaster />
      </QueryClientProvider>
    </IntlProvider>,
  );
  return { ...result, queryClient };
}
