"use client";

import { useQuery } from "@tanstack/react-query";
import { currentUserQueryKey, fetchCurrentUser } from "../api";

/** Who is signed in, from `GET /api/auth/me`; `data` is null without a session. */
export function useCurrentUser() {
  return useQuery({
    queryKey: currentUserQueryKey,
    queryFn: fetchCurrentUser,
    staleTime: 60_000,
    retry: false,
  });
}
