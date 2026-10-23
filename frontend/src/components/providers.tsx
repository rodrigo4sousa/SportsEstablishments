"use client";

import type { ReactNode } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "@/components/ui/sonner";

let browserQueryClient: QueryClient | undefined;

function getQueryClient() {
  // One client per server render, so requests never share cached data; one for the whole browser session.
  if (typeof window === "undefined") return new QueryClient();
  browserQueryClient ??= new QueryClient();
  return browserQueryClient;
}

/** TanStack Query for API data, and the toaster for mutation errors (docs/stack.md). */
export function Providers({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={getQueryClient()}>
      {children}
      <Toaster richColors position="top-center" />
    </QueryClientProvider>
  );
}
