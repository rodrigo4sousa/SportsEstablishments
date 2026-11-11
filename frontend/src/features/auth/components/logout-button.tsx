"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api/client";
import { UNKNOWN_ERROR, useApiErrors } from "@/lib/api/error-messages";
import { LOGIN_PATH } from "@/lib/auth/redirects";
import { logout } from "../api";

/** US03: ends the session, forgets everything fetched for this user and goes to the login page. */
export function LogoutButton({ size = "sm" }: { size?: "sm" | "cta" }) {
  const t = useTranslations("auth");
  const apiErrors = useApiErrors();
  const router = useRouter();
  const queryClient = useQueryClient();
  const mutation = useMutation({
    mutationFn: logout,
    onSuccess: () => {
      // Nothing fetched for this user may survive the logout (the next person on this browser could see it).
      queryClient.clear();
      router.replace(LOGIN_PATH);
    },
    onError: (error) => {
      toast.error(apiErrors.message(error instanceof ApiError ? error.code : UNKNOWN_ERROR));
    },
  });

  return (
    <Button type="button" variant="outline" size={size} disabled={mutation.isPending} onClick={() => mutation.mutate()}>
      {t("logout")}
    </Button>
  );
}
