"use client";

import { useQuery } from "@tanstack/react-query";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api/client";
import { errorMessage } from "@/lib/api/error-messages";
import { PitchSection } from "@/features/pitches/components/pitch-section";
import { clubQueryKey, fetchClub } from "../api";
import { ClubForm } from "./club-form";
import { ClubNotFound } from "./club-not-found";

/** US06: loads one of the owner's clubs and shows its edit form; a 404 (missing, or another owner's) is the not-found page. */
export function EditClub({ clubId }: { clubId: string }) {
  const club = useQuery({
    queryKey: clubQueryKey(clubId),
    queryFn: () => fetchClub(clubId),
    // A missing club stays missing, and the form must not be refreshed under the owner's hands while they type.
    retry: false,
    refetchOnWindowFocus: false,
  });

  if (club.isPending) {
    return (
      <p role="status" className="text-muted-foreground">
        A carregar o clube…
      </p>
    );
  }

  if (club.isError) {
    if (club.error instanceof ApiError && club.error.status === 404) {
      return <ClubNotFound />;
    }
    const code = club.error instanceof ApiError ? club.error.code : "unknown_error";
    return (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{errorMessage(code)}</p>
        <Button type="button" variant="outline" onClick={() => void club.refetch()}>
          Tentar novamente
        </Button>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-10">
      <ClubForm club={club.data} />
      <PitchSection clubId={club.data.id} />
    </div>
  );
}
