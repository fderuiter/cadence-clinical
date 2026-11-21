import * as fs from "fs";
import * as path from "path";
import { test as setup, expect } from "@playwright/test";
import { signInWithKeycloak } from "./helpers/keycloakLogin";

const authFile = "playwright/.auth/user.json";

/**
 * Signs in once through Keycloak when `E2E_KEYCLOAK=true` targets a live Docker
 * Keycloak realm, or verifies the compile-time `--mode demo` session in
 * standalone CI/accessibility runs where no identity provider is running.
 */
setup("authenticate workspace session", async ({ page }) => {
  fs.mkdirSync(path.dirname(authFile), { recursive: true });

  if (process.env.E2E_KEYCLOAK === "true") {
    const username = process.env.E2E_USERNAME || "designer.lead";
    await signInWithKeycloak(page, username);
  }

  await page.goto("/cadence-clinical/");
  await page.waitForLoadState("networkidle");
  await expect(page).not.toHaveURL(/.*login/);

  await page.context().storageState({ path: authFile });
});
