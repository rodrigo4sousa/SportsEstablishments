"use client";

import { useQuery } from "@tanstack/react-query";
import { Skeleton } from "@/components/ui/skeleton";
import { clubQueryKey, fetchClub } from "../api";

/**
 * The name of the club an owner page is about (UX-012), read through the same query as the club's page, so it is one request
 * for the page. Its height is reserved while it loads, and nothing shows if the club cannot be read (the page says so itself).
 */
export function ClubName({ clubId }: { clubId: string }) {
  const club = useQuery({ queryKey: clubQueryKey(clubId), queryFn: () => fetchClub(clubId), retry: false });

  if (club.isPending) {
    return <Skeleton className="h-7 w-48" />;
  }
  if (club.isError) {
    return null;
  }
  return <p className="text-lg font-medium">{club.data.name}</p>;
}
