import { apiRequest } from "@/lib/api/client";

export const EXPORT_FILE_NAME = "os-meus-dados.json";

/** US30: asks for the person's data and hands the browser the file to save. */
export async function downloadMyData(): Promise<void> {
  const response = await apiRequest("/api/me/data-export");
  const url = URL.createObjectURL(await response.blob());
  const link = document.createElement("a");
  link.href = url;
  link.download = EXPORT_FILE_NAME;
  document.body.append(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

/**
 * US30: deletes the signed-in account (204, and the API clears both cookies). 400 `validation_failed` or `invalid_password` on
 * `password`; 422 `owner_has_clubs` or `admin_cannot_delete_account`.
 */
export async function deleteMyAccount(password: string): Promise<void> {
  await apiRequest("/api/me/delete-account", { method: "POST", body: JSON.stringify({ password }) });
}
