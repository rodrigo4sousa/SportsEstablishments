import { readFile } from "node:fs/promises";
import { expect, test } from "@playwright/test";

// Flow 4 (US30): someone signs up with a throwaway address, downloads their data and deletes the account; after that the address cannot
// sign in. The address is new on every run and the test removes its own account, so it can run again on the same database.
test("a person exports their data, deletes the account and can no longer sign in", async ({ page }) => {
  const email = `e2e-${Date.now()}-${Math.random().toString(36).slice(2, 8)}@example.test`;
  const password = "e2e-throwaway-password";

  await test.step("sign up", async () => {
    await page.goto("/register");
    await page.getByLabel("Nome").fill("Pessoa de Teste");
    await page.getByLabel("Email").fill(email);
    await page.getByLabel("Palavra-passe").fill(password);
    await page.getByRole("checkbox", { name: /Li e aceito os Termos/ }).check();
    await page.getByRole("button", { name: "Criar conta", exact: true }).click();
    await expect(page).not.toHaveURL(/\/register/);
  });

  await test.step("export the data", async () => {
    await page.getByRole("link", { name: "A minha conta" }).click();
    await expect(page.getByRole("heading", { name: "Privacidade e dados" })).toBeVisible();

    const saved = page.waitForEvent("download");
    await page.getByRole("button", { name: "Exportar os meus dados" }).click();
    const download = await saved;

    expect(download.suggestedFilename()).toBe("os-meus-dados.json");
    const data = JSON.parse(await readFile(await download.path(), "utf8")) as { account: { email: string; name: string }; bookings: unknown[] };
    expect(data.account.email).toBe(email);
    expect(data.account.name).toBe("Pessoa de Teste");
    expect(data.bookings).toEqual([]);
    await expect(page.getByText("Os teus dados foram descarregados")).toBeVisible();
  });

  await test.step("delete the account", async () => {
    await page.getByRole("button", { name: "Apagar a minha conta" }).click();
    // Inside the confirmation: a page visited before (the sign-up form) stays mounted and hidden, with its own password field.
    await page.getByRole("group", { name: "Confirmar o apagamento da conta" }).getByLabel("Palavra-passe", { exact: true }).fill(password);
    await page.getByRole("button", { name: "Sim, apagar a minha conta" }).click();
    await expect(page.getByRole("heading", { name: "Conta apagada" })).toBeVisible();
  });

  await test.step("the address can no longer sign in", async () => {
    await page.goto("/login");
    await page.getByLabel("Email").fill(email);
    await page.getByLabel("Palavra-passe").fill(password);
    await page.getByRole("button", { name: "Entrar", exact: true }).click();
    await expect(page.getByText("Email ou palavra-passe incorretos.")).toBeVisible();
    await expect(page).toHaveURL(/\/login/);
  });
});
